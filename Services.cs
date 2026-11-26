using System.Media;
using System.Runtime.InteropServices;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace AudioProfileSwitcher;

public readonly record struct ApplyResult(
    bool AnyApplied,
    string? Problem,
    AudioSnapshot? Snapshot = null,
    Guid? ResolvedOutputId = null,
    Guid? ResolvedCommOutputId = null,
    Guid? ResolvedInputId = null);

public readonly record struct CycleResult(DeviceItem? Device, AudioSnapshot? Snapshot);

public readonly record struct MuteToggleResult(bool? Muted, AudioSnapshot? Snapshot);

/// <summary>All Core Audio access lives here, serialized off the UI thread with event-driven notifications.</summary>
public sealed class AudioService : IDisposable
{
    private readonly CoreAudioController _audio = new();
    private readonly object _lock = new();
    private IDisposable? _controllerSub;
    private IDisposable? _captureMuteSub;
    private Guid? _watchedCaptureId;
    private bool _disposed;

    /// <summary>Fired (potentially off the UI thread) when endpoints, defaults, states, or mute state change.</summary>
    public event Action? StateChanged;

    public AudioService()
    {
        try
        {
            lock (_lock)
            {
                _controllerSub = _audio.AudioDeviceChanged.Subscribe(new ActionObserver<DeviceChangedArgs>(OnControllerDeviceChanged));
                EnsureCaptureMuteSubscriptionLocked(_audio.DefaultCaptureDevice);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to subscribe to CoreAudio notifications; falling back to safety polling", ex);
        }
    }

    private void OnControllerDeviceChanged(DeviceChangedArgs e)
    {
        if (e == null) return;

        bool relevant = e.ChangedType is
            DeviceChangedType.DefaultChanged or
            DeviceChangedType.DeviceAdded or
            DeviceChangedType.DeviceRemoved or
            DeviceChangedType.StateChanged or
            DeviceChangedType.MuteChanged;
        if (!relevant) return;

        bool notify = false;
        lock (_lock)
        {
            if (_disposed) return;
            if (e.ChangedType is DeviceChangedType.DefaultChanged or DeviceChangedType.DeviceAdded or
                DeviceChangedType.DeviceRemoved or DeviceChangedType.StateChanged)
            {
                try { EnsureCaptureMuteSubscriptionLocked(_audio.DefaultCaptureDevice); }
                catch { /* endpoint may still be settling */ }
            }
            notify = true;
        }

        if (notify) RaiseStateChanged();
    }

    private void EnsureCaptureMuteSubscriptionLocked(CoreAudioDevice? capture)
    {
        var newId = capture?.Id;
        if (_watchedCaptureId == newId && (_captureMuteSub != null || newId == null))
            return;

        try { _captureMuteSub?.Dispose(); } catch { }
        _captureMuteSub = null;
        _watchedCaptureId = newId;

        if (capture != null)
        {
            _captureMuteSub = capture.MuteChanged.Subscribe(
                new ActionObserver<DeviceMuteChangedArgs>(_ => RaiseStateChanged()));
        }
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { AppLog.Error("AudioService StateChanged handler failed", ex); }
    }

    private List<CoreAudioDevice> Outputs() =>
        _audio.GetPlaybackDevices(DeviceState.Active)
              .OrderBy(d => d.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

    private List<CoreAudioDevice> Inputs() =>
        _audio.GetCaptureDevices(DeviceState.Active)
              .OrderBy(d => d.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static int SafeVolumePercent(CoreAudioDevice d)
    {
        try
        {
            double v = d.Volume;
            return double.IsNaN(v) || double.IsInfinity(v) ? 80 : Math.Clamp((int)Math.Round(v), 0, 100);
        }
        catch { return 80; }
    }

    public Task<(List<DeviceItem> Outputs, List<DeviceItem> Inputs)> GetDeviceItemsAsync() =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return (new List<DeviceItem>(), new List<DeviceItem>());
                return (
                    Outputs().Select(d => new DeviceItem(d.Id, d.FullName, true, SafeVolumePercent(d))).ToList(),
                    Inputs().Select(d => new DeviceItem(d.Id, d.FullName, true, SafeVolumePercent(d))).ToList()
                );
            }
        });

    private AudioSnapshot TakeSnapshotLocked()
    {
        var outs = Outputs();
        var ins = Inputs();
        var dOut = _audio.DefaultPlaybackDevice;
        var dCommOut = _audio.DefaultPlaybackCommunicationsDevice;
        var dIn = _audio.DefaultCaptureDevice;
        EnsureCaptureMuteSubscriptionLocked(dIn);

        return new AudioSnapshot(
            outs.Select(d => d.Id).ToHashSet(),
            ins.Select(d => d.Id).ToHashSet(),
            outs.ToDictionary(d => d.Id, d => d.FullName),
            ins.ToDictionary(d => d.Id, d => d.FullName),
            dOut?.Id,
            dIn?.Id,
            dOut?.FullName,
            dIn?.FullName,
            dIn?.IsMuted ?? false,
            dCommOut?.Id,
            dCommOut?.FullName);
    }

    public Task<AudioSnapshot?> SnapshotAsync() =>
        Task.Run<AudioSnapshot?>(() =>
        {
            lock (_lock)
            {
                if (_disposed) return null;
                return TakeSnapshotLocked();
            }
        });

    private static void SetDefault(CoreAudioDevice d, bool includeComm = true)
    {
        if (!d.IsDefaultDevice) d.SetAsDefault();
        if (includeComm && !d.IsDefaultCommunicationsDevice) d.SetAsDefaultCommunications();
    }

    private static void SetDefaultCommOnly(CoreAudioDevice d)
    {
        if (!d.IsDefaultCommunicationsDevice) d.SetAsDefaultCommunications();
    }

    private static void ApplyVolumeIfNeeded(CoreAudioDevice d, int? targetVolume)
    {
        if (!targetVolume.HasValue) return;
        int clamped = Math.Clamp(targetVolume.Value, 0, 100);
        if (Math.Abs(d.Volume - clamped) >= 0.5)
        {
            d.Volume = clamped;
        }
    }

    private static CoreAudioDevice? FindDevice(List<CoreAudioDevice> list, Guid id, string? savedName, out bool matchedByName)
    {
        matchedByName = false;
        var exact = list.FirstOrDefault(x => x.Id == id);
        if (exact != null) return exact;

        if (ProfileLogic.IsMeaningfulDeviceName(savedName))
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

    public Task<ApplyResult> ApplyAsync(
        Guid? outputId,
        string? outputName,
        Guid? inputId,
        string? inputName,
        Guid? commOutputId = null,
        string? commOutputName = null,
        int? outputVolume = null,
        int? inputVolume = null) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return new ApplyResult(false, "audio service closed");

                var problems = new List<string>();
                bool any = false;
                Guid? resolvedOut = null;
                Guid? resolvedCommOut = null;
                Guid? resolvedIn = null;

                List<CoreAudioDevice>? cachedOuts = (outputId.HasValue || commOutputId.HasValue) ? Outputs() : null;
                bool hasSeparateComm = commOutputId.HasValue;

                if (outputId.HasValue)
                {
                    var d = FindDevice(cachedOuts!, outputId.Value, outputName, out bool byName);
                    if (d == null)
                    {
                        problems.Add("output not connected");
                    }
                    else
                    {
                        SetDefault(d, includeComm: !hasSeparateComm);
                        ApplyVolumeIfNeeded(d, outputVolume);
                        any = true;
                        if (byName) resolvedOut = d.Id;
                    }
                }
                else if (outputVolume.HasValue && _audio.DefaultPlaybackDevice is { } curOut)
                {
                    ApplyVolumeIfNeeded(curOut, outputVolume);
                    any = true;
                }

                if (commOutputId.HasValue)
                {
                    var comm = FindDevice(cachedOuts!, commOutputId.Value, commOutputName, out bool byName);
                    if (comm == null)
                    {
                        problems.Add("comm output not connected");
                    }
                    else
                    {
                        SetDefaultCommOnly(comm);
                        any = true;
                        if (byName) resolvedCommOut = comm.Id;
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
                        SetDefault(d, includeComm: true);
                        ApplyVolumeIfNeeded(d, inputVolume);
                        any = true;
                        if (byName) resolvedIn = d.Id;
                    }
                }
                else if (inputVolume.HasValue && _audio.DefaultCaptureDevice is { } curIn)
                {
                    ApplyVolumeIfNeeded(curIn, inputVolume);
                    any = true;
                }

                var snap = any ? TakeSnapshotLocked() : null;
                return new ApplyResult(
                    any,
                    problems.Count == 0 ? null : string.Join(", ", problems),
                    snap,
                    resolvedOut,
                    resolvedCommOut,
                    resolvedIn);
            }
        });

    public Task<CycleResult> CycleOutputAsync(IReadOnlyCollection<CycleDeviceEntry> filter) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return new CycleResult(null, null);
                var next = Cycle(Outputs(), _audio.DefaultPlaybackDevice, filter);
                if (next == null) return new CycleResult(null, null);
                return new CycleResult(new DeviceItem(next.Id, next.FullName), TakeSnapshotLocked());
            }
        });

    public Task<CycleResult> CycleInputAsync(IReadOnlyCollection<CycleDeviceEntry> filter) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return new CycleResult(null, null);
                var next = Cycle(Inputs(), _audio.DefaultCaptureDevice, filter);
                if (next == null) return new CycleResult(null, null);
                return new CycleResult(new DeviceItem(next.Id, next.FullName), TakeSnapshotLocked());
            }
        });

    private static CoreAudioDevice? Cycle(
        List<CoreAudioDevice> all,
        CoreAudioDevice? current,
        IReadOnlyCollection<CycleDeviceEntry> filter)
    {
        var pairs = all.Select(d => (d.Id, d.FullName)).ToList();
        int nextIdx = ProfileLogic.SelectNextCycleIndex(pairs, current?.Id, filter);
        if (nextIdx < 0) return null;

        var next = all[nextIdx];
        SetDefault(next, includeComm: true);
        return next;
    }

    /// <summary>Toggles default microphone mute and returns the new state alongside a fresh snapshot.</summary>
    public Task<MuteToggleResult> ToggleMuteAsync() =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_disposed) return new MuteToggleResult(null, null);
                var mic = _audio.DefaultCaptureDevice;
                if (mic == null) return new MuteToggleResult(null, null);
                bool nowMuted = !mic.IsMuted;
                mic.Mute(nowMuted);
                return new MuteToggleResult(nowMuted, TakeSnapshotLocked());
            }
        });

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _captureMuteSub?.Dispose(); } catch { }
            try { _controllerSub?.Dispose(); } catch { }
            _captureMuteSub = null;
            _controllerSub = null;
            _audio.Dispose();
        }
    }

    private sealed class ActionObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) => AppLog.Error("CoreAudio observable error", error);
        public void OnCompleted() { }
    }
}

internal sealed class HotKeyService : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly List<int> _slotIds = new();
    private List<(string Name, HotKeyModifiers Modifiers, Keys Key)>? _registeredSpec;
    private List<string> _lastFailures = new();
    private int _nextId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeyService()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>
    /// Registers hotkeys with Win32, skipping UnregisterHotKey/RegisterHotKey churn when the spec
    /// is unchanged (unless <paramref name="force"/> is true, e.g. after power/session resume).
    /// </summary>
    public List<string> RegisterAll(
        IReadOnlyList<(string Name, HotKeyBinding Binding, Action Action)> items,
        bool force = false)
    {
        if (!force && _registeredSpec != null && _lastFailures.Count == 0 && SpecMatches(_registeredSpec, items))
        {
            for (int i = 0; i < _slotIds.Count; i++)
            {
                _actions[_slotIds[i]] = items[i].Action;
            }
            return _lastFailures;
        }

        UnregisterAll();
        var spec = new List<(string Name, HotKeyModifiers Modifiers, Keys Key)>(items.Count);
        var failures = new List<string>();

        foreach (var (name, binding, action) in items)
        {
            spec.Add((name, binding.Modifiers, binding.Key));
            int id = _nextId++;
            // NoRepeat: holding the keys down must not spam-cycle devices.
            uint mods = (uint)(binding.Modifiers | HotKeyModifiers.NoRepeat);
            if (RegisterHotKey(Handle, id, mods, (uint)binding.Key))
            {
                _actions[id] = action;
                _slotIds.Add(id);
            }
            else
            {
                failures.Add($"{name} ({binding.Format()})");
            }
        }

        _registeredSpec = spec;
        _lastFailures = failures;
        return failures;
    }

    private static bool SpecMatches(
        List<(string Name, HotKeyModifiers Modifiers, Keys Key)> prev,
        IReadOnlyList<(string Name, HotKeyBinding Binding, Action Action)> next)
    {
        if (prev.Count != next.Count) return false;
        for (int i = 0; i < prev.Count; i++)
        {
            if (prev[i].Modifiers != next[i].Binding.Modifiers ||
                prev[i].Key != next[i].Binding.Key ||
                !string.Equals(prev[i].Name, next[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(Handle, id);
        _actions.Clear();
        _slotIds.Clear();
        _registeredSpec = null;
        _lastFailures = new();
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
    private static readonly object PlayLock = new();
    private static readonly Dictionary<ChimeCue, PreloadedCue> Cues = BuildPreloadedCues();

    private sealed class PreloadedCue
    {
        public MemoryStream Stream { get; }
        public SoundPlayer Player { get; }

        public PreloadedCue(byte[] wavBytes)
        {
            Stream = new MemoryStream(wavBytes, writable: false);
            Player = new SoundPlayer(Stream);
            Player.Load();
        }
    }

    private static Dictionary<ChimeCue, PreloadedCue> BuildPreloadedCues()
    {
        var dict = new Dictionary<ChimeCue, PreloadedCue>(3);
        foreach (var cue in Enum.GetValues<ChimeCue>())
        {
            try
            {
                dict[cue] = new PreloadedCue(ChimeSynth.BuildWav(cue));
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to preload chime cue {cue}", ex);
            }
        }
        return dict;
    }

    public static void Play(ChimeCue cue = ChimeCue.Switch, bool waitForDeviceRouting = false)
    {
        if (!waitForDeviceRouting)
        {
            PlayNow(cue);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120).ConfigureAwait(false); // let Windows finish routing the new default output
                PlayNow(cue);
            }
            catch (Exception ex)
            {
                AppLog.Error("Chime playback failed", ex);
            }
        });
    }

    private static void PlayNow(ChimeCue cue)
    {
        lock (PlayLock)
        {
            try
            {
                if (Cues.TryGetValue(cue, out var loaded))
                {
                    loaded.Player.Play(); // non-blocking playback from preloaded WAV buffer
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"Chime playback failed ({cue})", ex);
            }
        }
    }
}