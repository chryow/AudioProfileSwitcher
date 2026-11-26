using System.Media;
using System.Runtime.InteropServices;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace AudioProfileSwitcher;

public sealed record AudioSnapshot(
    HashSet<Guid> OutputIds,
    HashSet<Guid> InputIds,
    Dictionary<Guid, string> OutputNames,
    Dictionary<Guid, string> InputNames,
    Guid? DefaultOut,
    Guid? DefaultIn,
    string? OutName,
    string? InName,
    bool MicMuted)
{
    /// <summary>Changes whenever anything the UI shows could have changed.</summary>
    public string Signature =>
        string.Join(",", OutputIds.OrderBy(x => x)) + "|" +
        string.Join(",", InputIds.OrderBy(x => x)) +
        $"|{DefaultOut}|{DefaultIn}|{MicMuted}|{OutName}|{InName}";
}

public readonly record struct ApplyResult(
    bool AnyApplied,
    string? Problem,
    Guid? ResolvedOutputId = null,
    Guid? ResolvedInputId = null);

/// <summary>All Core Audio access lives here, serialized off the UI thread.</summary>
public sealed class AudioService : IDisposable
{
    private readonly CoreAudioController _audio = new();
    private readonly object _lock = new();
    private bool _disposed;

    private List<CoreAudioDevice> Outputs() =>
        _audio.GetPlaybackDevices(DeviceState.Active)
              .OrderBy(d => d.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

    private List<CoreAudioDevice> Inputs() =>
        _audio.GetCaptureDevices(DeviceState.Active)
              .OrderBy(d => d.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

    public Task<(List<DeviceItem> Outputs, List<DeviceItem> Inputs)> GetDeviceItemsAsync() =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return (new List<DeviceItem>(), new List<DeviceItem>());
                return (
                    Outputs().Select(d => new DeviceItem(d.Id, d.FullName)).ToList(),
                    Inputs().Select(d => new DeviceItem(d.Id, d.FullName)).ToList()
                );
            }
        });

    public Task<AudioSnapshot> SnapshotAsync() =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                var outs = Outputs();
                var ins = Inputs();
                var dOut = _audio.DefaultPlaybackDevice;
                var dIn = _audio.DefaultCaptureDevice;
                return new AudioSnapshot(
                    outs.Select(d => d.Id).ToHashSet(),
                    ins.Select(d => d.Id).ToHashSet(),
                    outs.ToDictionary(d => d.Id, d => d.FullName),
                    ins.ToDictionary(d => d.Id, d => d.FullName),
                    dOut?.Id, dIn?.Id,
                    dOut?.FullName, dIn?.FullName,
                    dIn?.IsMuted ?? false);
            }
        });

    private static void SetDefault(CoreAudioDevice d)
    {
        d.SetAsDefault();
        d.SetAsDefaultCommunications();
    }

    private static CoreAudioDevice? FindDevice(List<CoreAudioDevice> list, Guid id, string? savedName, out bool matchedByName)
    {
        matchedByName = false;
        var exact = list.FirstOrDefault(x => x.Id == id);
        if (exact != null) return exact;

        if (!string.IsNullOrWhiteSpace(savedName) && !savedName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            var byName = list.FirstOrDefault(x => x.FullName.Equals(savedName, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                matchedByName = true;
                return byName;
            }
        }
        return null;
    }

    public Task<ApplyResult> ApplyAsync(Guid? outputId, string? outputName, Guid? inputId, string? inputName) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return new ApplyResult(false, "audio service closed");

                var problems = new List<string>();
                bool any = false;
                Guid? resolvedOut = null;
                Guid? resolvedIn = null;

                if (outputId.HasValue)
                {
                    var d = FindDevice(Outputs(), outputId.Value, outputName, out bool byName);
                    if (d == null)
                    {
                        problems.Add("output not connected");
                    }
                    else
                    {
                        SetDefault(d);
                        any = true;
                        if (byName) resolvedOut = d.Id;
                    }
                }

                if (inputId.HasValue)
                {
                    var d = FindDevice(Inputs(), inputId.Value, inputName, out bool byName);
                    if (d == null)
                    {
                        problems.Add("input not connected");
                    }
                    else
                    {
                        SetDefault(d);
                        any = true;
                        if (byName) resolvedIn = d.Id;
                    }
                }

                return new ApplyResult(any, problems.Count == 0 ? null : string.Join(", ", problems), resolvedOut, resolvedIn);
            }
        });

    public Task<DeviceItem?> CycleOutputAsync(IReadOnlyCollection<Guid> filter) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                var next = Cycle(Outputs(), _audio.DefaultPlaybackDevice, filter);
                return next == null ? null : new DeviceItem(next.Id, next.FullName);
            }
        });

    public Task<DeviceItem?> CycleInputAsync(IReadOnlyCollection<Guid> filter) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                var next = Cycle(Inputs(), _audio.DefaultCaptureDevice, filter);
                return next == null ? null : new DeviceItem(next.Id, next.FullName);
            }
        });

    private static CoreAudioDevice? Cycle(List<CoreAudioDevice> all, CoreAudioDevice? current, IReadOnlyCollection<Guid> filter)
    {
        var devices = filter.Count > 0 ? all.Where(d => filter.Contains(d.Id)).ToList() : all;
        if (devices.Count < 2) return null;

        int idx = current == null ? -1 : devices.FindIndex(d => d.Id == current.Id);
        var next = devices[(idx + 1) % devices.Count];
        SetDefault(next);
        return next;
    }

    /// <summary>Returns the new muted state, or null when there is no default microphone.</summary>
    public Task<bool?> ToggleMuteAsync() =>
        Task.Run<bool?>(() =>
        {
            lock (_lock)
            {
                var mic = _audio.DefaultCaptureDevice;
                if (mic == null) return null;
                bool nowMuted = !mic.IsMuted;
                mic.Mute(nowMuted);
                return nowMuted;
            }
        });

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _audio.Dispose();
        }
    }
}

internal sealed class HotKeyService : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeyService()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>Registers everything; returns the names of hotkeys another app already owns.</summary>
    public List<string> RegisterAll(IEnumerable<(string Name, HotKeyBinding Binding, Action Action)> items)
    {
        UnregisterAll();
        var failures = new List<string>();
        foreach (var (name, binding, action) in items)
        {
            int id = _nextId++;
            // NoRepeat: holding the keys down must not spam-cycle devices.
            uint mods = (uint)(binding.Modifiers | HotKeyModifiers.NoRepeat);
            if (RegisterHotKey(Handle, id, mods, (uint)binding.Key))
                _actions[id] = action;
            else
                failures.Add($"{name} ({binding.Format()})");
        }
        return failures;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            try { action(); }
            catch (Exception ex) { AppLog.Error("Hotkey handler failed", ex); }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}

internal static class ChimePlayer
{
    private static readonly byte[] ChimeWav = GenerateSoftChime();

    public static void PlayAsync()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120); // let Windows finish routing the new default output
                using var ms = new MemoryStream(ChimeWav);
                using var player = new SoundPlayer(ms);
                player.PlaySync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Chime playback failed", ex);
            }
        });
    }

    private static byte[] GenerateSoftChime()
    {
        const int sampleRate = 44100;
        const double durationSec = 0.16;
        int sampleCount = (int)(sampleRate * durationSec);
        int dataSize = sampleCount * 2;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataSize);

        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            double env = Math.Sin(Math.PI * (i / (double)sampleCount));
            double freq = t < 0.08 ? 660.0 : 880.0; // E5 -> A5
            short sample = (short)(Math.Sin(2.0 * Math.PI * freq * t) * env * 6000);
            bw.Write(sample);
        }

        return ms.ToArray();
    }
}