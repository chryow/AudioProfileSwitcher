using System.Media;
using System.Runtime.InteropServices;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace AudioProfileSwitcher;

public sealed record AudioSnapshot(
    HashSet<Guid> OutputIds,
    HashSet<Guid> InputIds,
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

public readonly record struct ApplyResult(bool AnyApplied, string? Problem);

/// <summary>All Core Audio access lives here so the UI never touches the library directly.</summary>
public sealed class AudioService : IDisposable
{
    private readonly CoreAudioController _audio = new();

    private List<CoreAudioDevice> Outputs() =>
        _audio.GetPlaybackDevices(DeviceState.Active)
              .OrderBy(d => d.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

    private List<CoreAudioDevice> Inputs() =>
        _audio.GetCaptureDevices(DeviceState.Active)
              .OrderBy(d => d.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

    public List<DeviceItem> OutputItems() => Outputs().Select(d => new DeviceItem(d.Id, d.FullName)).ToList();
    public List<DeviceItem> InputItems() => Inputs().Select(d => new DeviceItem(d.Id, d.FullName)).ToList();

    public AudioSnapshot Snapshot()
    {
        var outs = Outputs();
        var ins = Inputs();
        var dOut = _audio.DefaultPlaybackDevice;
        var dIn = _audio.DefaultCaptureDevice;
        return new AudioSnapshot(
            outs.Select(d => d.Id).ToHashSet(),
            ins.Select(d => d.Id).ToHashSet(),
            dOut?.Id, dIn?.Id,
            dOut?.FullName, dIn?.FullName,
            dIn?.IsMuted ?? false);
    }

    private static void SetDefault(CoreAudioDevice d)
    {
        d.SetAsDefault();
        d.SetAsDefaultCommunications();
    }

    public ApplyResult Apply(Guid? outputId, Guid? inputId)
    {
        var problems = new List<string>();
        bool any = false;

        if (outputId.HasValue)
        {
            var d = Outputs().FirstOrDefault(x => x.Id == outputId.Value);
            if (d == null) problems.Add("output not connected");
            else { SetDefault(d); any = true; }
        }

        if (inputId.HasValue)
        {
            var d = Inputs().FirstOrDefault(x => x.Id == inputId.Value);
            if (d == null) problems.Add("input not connected");
            else { SetDefault(d); any = true; }
        }

        return new ApplyResult(any, problems.Count == 0 ? null : string.Join(", ", problems));
    }

    public CoreAudioDevice? CycleOutput(IReadOnlyCollection<Guid> filter) =>
        Cycle(Outputs(), _audio.DefaultPlaybackDevice, filter);

    public CoreAudioDevice? CycleInput(IReadOnlyCollection<Guid> filter) =>
        Cycle(Inputs(), _audio.DefaultCaptureDevice, filter);

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
    public bool? ToggleMute()
    {
        var mic = _audio.DefaultCaptureDevice;
        if (mic == null) return null;
        bool nowMuted = !mic.IsMuted;
        mic.Mute(nowMuted);
        return nowMuted;
    }

    public void Dispose() => _audio.Dispose();
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
            try { action(); } catch { /* never let a hotkey handler crash the message loop */ }
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
            catch { }
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
