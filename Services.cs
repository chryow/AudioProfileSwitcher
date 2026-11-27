using System.Diagnostics;
using System.Media;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

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

/// <summary>
/// All Windows CoreAudio access lives here via NAudio.Wasapi + native IPolicyConfig COM interop,
/// serialized off the UI thread with event-driven IMMNotificationClient &amp; endpoint mute callbacks.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly NotificationClient _notifClient;
    private readonly object _lock = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _watchedCaptureDevice;
    private string? _watchedCaptureRawId;
    private int _watchedCaptureMuted = -1; // -1 = unknown, 0 = live, 1 = muted
    private bool _notifRegistered;
    private int _disposedFlag;

    private bool IsDisposed => Volatile.Read(ref _disposedFlag) != 0;

    /// <summary>Fired (off the UI thread) when endpoints, defaults, states, or mic mute state change.</summary>
    public event Action? StateChanged;

    private sealed record EndpointEntry(Guid Id, string RawId, string FullName, MMDevice Device);

    public AudioService()
    {
        _notifClient = new NotificationClient(this);
        // Initialize CoreAudio COM objects strictly on an MTA ThreadPool thread (never on the UI STA thread).
        _ = Task.Run(() =>
        {
            lock (_lock)
            {
                if (IsDisposed) return;
                EnsureEnumeratorLocked();
            }
        });
    }

    private MMDeviceEnumerator? EnsureEnumeratorLocked()
    {
        if (IsDisposed) return null;
        if (_enumerator != null) return _enumerator;

        try
        {
            var enumerator = new MMDeviceEnumerator();
            _enumerator = enumerator;
            try
            {
                enumerator.RegisterEndpointNotificationCallback(_notifClient);
                _notifRegistered = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to subscribe to CoreAudio notifications; falling back to safety polling", ex);
            }
            return enumerator;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to create MMDeviceEnumerator", ex);
            return null;
        }
    }

    /// <summary>
    /// Called directly on Windows AudioSes COM callback threads.
    /// MUST NEVER acquire _lock or call CoreAudio COM methods here, or Windows AudioSes will AB-BA deadlock
    /// with SetDefaultEndpoint / UnregisterControlChangeNotify.
    /// </summary>
    private void OnEndpointNotificationLockFree()
    {
        if (IsDisposed) return;
        ThreadPool.QueueUserWorkItem(_ => RaiseStateChanged());
    }

    private void EnsureCaptureMuteSubscriptionLocked(string? captureRawId)
    {
        if (string.Equals(_watchedCaptureRawId, captureRawId, StringComparison.OrdinalIgnoreCase) &&
            (_watchedCaptureDevice != null || captureRawId == null))
        {
            return;
        }

        ReleaseCaptureWatchLocked();
        _watchedCaptureRawId = captureRawId;

        if (!string.IsNullOrEmpty(captureRawId) && _enumerator != null)
        {
            try
            {
                var dev = _enumerator.GetDevice(captureRawId);
                Interlocked.Exchange(ref _watchedCaptureMuted, dev.AudioEndpointVolume.Mute ? 1 : 0);
                dev.AudioEndpointVolume.OnVolumeNotification += OnCaptureVolumeNotification;
                _watchedCaptureDevice = dev;
            }
            catch
            {
                _watchedCaptureRawId = null;
            }
        }
    }

    private void ReleaseCaptureWatchLocked()
    {
        if (_watchedCaptureDevice != null)
        {
            try { _watchedCaptureDevice.AudioEndpointVolume.OnVolumeNotification -= OnCaptureVolumeNotification; } catch { }
            try { _watchedCaptureDevice.Dispose(); } catch { }
            _watchedCaptureDevice = null;
        }
    }

    /// <summary>Lock-free AudioEndpointVolume callback; never acquires _lock or makes COM calls.</summary>
    private void OnCaptureVolumeNotification(AudioVolumeNotificationData data)
    {
        if (IsDisposed) return;
        int next = data.Muted ? 1 : 0;
        int prev = Interlocked.Exchange(ref _watchedCaptureMuted, next);
        if (prev != next)
        {
            ThreadPool.QueueUserWorkItem(_ => RaiseStateChanged());
        }
    }

    private void RaiseStateChanged()
    {
        if (IsDisposed) return;
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { AppLog.Error("AudioService StateChanged handler failed", ex); }
    }

    private MMDevice? TryGetDefaultEndpoint(DataFlow flow, Role role)
    {
        var enumerator = EnsureEnumeratorLocked();
        if (enumerator == null) return null;
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(flow, role)) return null;
            return enumerator.GetDefaultAudioEndpoint(flow, role);
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetDefaultRawId(DataFlow flow, Role role)
    {
        using var d = TryGetDefaultEndpoint(flow, role);
        return SafeGetRawId(d);
    }

    private static string? SafeGetRawId(MMDevice? d)
    {
        if (d == null) return null;
        try { return d.ID; } catch { return null; }
    }

    private static string? SafeGetFriendlyName(MMDevice? d)
    {
        if (d == null) return null;
        try { return d.FriendlyName; } catch { return null; }
    }

    private List<EndpointEntry> EnumerateEndpointsLocked(DataFlow flow)
    {
        var result = new List<EndpointEntry>();
        var enumerator = EnsureEnumeratorLocked();
        if (enumerator == null) return result;
        try
        {
            var col = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            for (int i = 0; i < col.Count; i++)
            {
                MMDevice? d = null;
                try
                {
                    d = col[i];
                    string rawId = d.ID;
                    string name = d.FriendlyName;
                    Guid id = ProfileLogic.ParseEndpointGuid(rawId);
                    result.Add(new EndpointEntry(id, rawId, name, d));
                    d = null; // ownership transferred to EndpointEntry
                }
                catch
                {
                    d?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to enumerate {flow} audio endpoints", ex);
        }

        result.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.FullName, b.FullName));
        return result;
    }

    private static void DisposeEntries(List<EndpointEntry>? entries)
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            try { e.Device.Dispose(); } catch { }
        }
    }

    private static int SafeVolumePercent(MMDevice d)
    {
        try
        {
            float scalar = d.AudioEndpointVolume.MasterVolumeLevelScalar;
            if (float.IsNaN(scalar) || float.IsInfinity(scalar)) return 80;
            return Math.Clamp((int)Math.Round(scalar * 100f), 0, 100);
        }
        catch { return 80; }
    }

    public Task<(List<DeviceItem> Outputs, List<DeviceItem> Inputs)> GetDeviceItemsAsync() =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (IsDisposed) return (new List<DeviceItem>(), new List<DeviceItem>());
                var outs = EnumerateEndpointsLocked(DataFlow.Render);
                var ins = EnumerateEndpointsLocked(DataFlow.Capture);
                try
                {
                    return (
                        outs.Select(e => new DeviceItem(e.Id, e.FullName, true, SafeVolumePercent(e.Device))).ToList(),
                        ins.Select(e => new DeviceItem(e.Id, e.FullName, true, SafeVolumePercent(e.Device))).ToList()
                    );
                }
                finally
                {
                    DisposeEntries(outs);
                    DisposeEntries(ins);
                }
            }
        });

    private AudioSnapshot TakeSnapshotLocked()
    {
        var outs = EnumerateEndpointsLocked(DataFlow.Render);
        var ins = EnumerateEndpointsLocked(DataFlow.Capture);
        using var dOut = TryGetDefaultEndpoint(DataFlow.Render, Role.Multimedia);
        using var dCommOut = TryGetDefaultEndpoint(DataFlow.Render, Role.Communications);
        using var dIn = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);

        try
        {
            string? dOutRaw = SafeGetRawId(dOut);
            string? dCommRaw = SafeGetRawId(dCommOut);
            string? dInRaw = SafeGetRawId(dIn);

            EnsureCaptureMuteSubscriptionLocked(dInRaw);

            bool micMuted = false;
            if (dIn != null)
            {
                try
                {
                    micMuted = dIn.AudioEndpointVolume.Mute;
                    Interlocked.Exchange(ref _watchedCaptureMuted, micMuted ? 1 : 0);
                }
                catch { }
            }

            Guid? defOutId = dOutRaw != null ? ProfileLogic.ParseEndpointGuid(dOutRaw) : null;
            Guid? defCommId = dCommRaw != null ? ProfileLogic.ParseEndpointGuid(dCommRaw) : null;
            Guid? defInId = dInRaw != null ? ProfileLogic.ParseEndpointGuid(dInRaw) : null;

            var outIds = new HashSet<Guid>(outs.Count);
            var outNames = new Dictionary<Guid, string>(outs.Count);
            foreach (var o in outs)
            {
                outIds.Add(o.Id);
                outNames[o.Id] = o.FullName;
            }

            var inIds = new HashSet<Guid>(ins.Count);
            var inNames = new Dictionary<Guid, string>(ins.Count);
            foreach (var i in ins)
            {
                inIds.Add(i.Id);
                inNames[i.Id] = i.FullName;
            }

            return new AudioSnapshot(
                outIds,
                inIds,
                outNames,
                inNames,
                defOutId,
                defInId,
                SafeGetFriendlyName(dOut),
                SafeGetFriendlyName(dIn),
                micMuted,
                defCommId,
                SafeGetFriendlyName(dCommOut));
        }
        finally
        {
            DisposeEntries(outs);
            DisposeEntries(ins);
        }
    }

    public Task<AudioSnapshot?> SnapshotAsync() =>
        Task.Run<AudioSnapshot?>(() =>
        {
            lock (_lock)
            {
                if (IsDisposed) return null;
                return TakeSnapshotLocked();
            }
        });

    private void SetDefaultLocked(DataFlow flow, string rawEndpointId, bool includeComm = true)
    {
        string? curMultimedia = TryGetDefaultRawId(flow, Role.Multimedia);
        string? curConsole = TryGetDefaultRawId(flow, Role.Console);

        if (!string.Equals(curConsole, rawEndpointId, StringComparison.OrdinalIgnoreCase))
            PolicyConfigClient.SetDefaultEndpoint(rawEndpointId, Role.Console);

        if (!string.Equals(curMultimedia, rawEndpointId, StringComparison.OrdinalIgnoreCase))
            PolicyConfigClient.SetDefaultEndpoint(rawEndpointId, Role.Multimedia);

        if (includeComm)
            SetDefaultCommOnlyLocked(flow, rawEndpointId);
    }

    private void SetDefaultCommOnlyLocked(DataFlow flow, string rawEndpointId)
    {
        string? curComm = TryGetDefaultRawId(flow, Role.Communications);
        if (!string.Equals(curComm, rawEndpointId, StringComparison.OrdinalIgnoreCase))
            PolicyConfigClient.SetDefaultEndpoint(rawEndpointId, Role.Communications);
    }

    private static void ApplyVolumeIfNeeded(MMDevice d, int? targetVolume)
    {
        if (!targetVolume.HasValue) return;
        float targetScalar = Math.Clamp(targetVolume.Value, 0, 100) / 100f;
        float currentScalar = d.AudioEndpointVolume.MasterVolumeLevelScalar;
        if (Math.Abs(currentScalar - targetScalar) >= 0.005f)
        {
            d.AudioEndpointVolume.MasterVolumeLevelScalar = targetScalar;
        }
    }

    private static EndpointEntry? FindDevice(List<EndpointEntry> list, Guid id, string? savedName, out bool matchedByName)
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
                if (IsDisposed) return new ApplyResult(false, "audio service closed");

                var problems = new List<string>();
                bool any = false;
                Guid? resolvedOut = null;
                Guid? resolvedCommOut = null;
                Guid? resolvedIn = null;

                List<EndpointEntry>? outs = (outputId.HasValue || commOutputId.HasValue)
                    ? EnumerateEndpointsLocked(DataFlow.Render)
                    : null;
                List<EndpointEntry>? ins = inputId.HasValue
                    ? EnumerateEndpointsLocked(DataFlow.Capture)
                    : null;

                try
                {
                    bool hasSeparateComm = commOutputId.HasValue;

                    if (outputId.HasValue)
                    {
                        var d = FindDevice(outs!, outputId.Value, outputName, out bool byName);
                        if (d == null)
                        {
                            problems.Add("output not connected");
                        }
                        else
                        {
                            SetDefaultLocked(DataFlow.Render, d.RawId, includeComm: !hasSeparateComm);
                            ApplyVolumeIfNeeded(d.Device, outputVolume);
                            any = true;
                            if (byName) resolvedOut = d.Id;
                        }
                    }
                    else if (outputVolume.HasValue)
                    {
                        using var curOut = TryGetDefaultEndpoint(DataFlow.Render, Role.Multimedia);
                        if (curOut != null)
                        {
                            ApplyVolumeIfNeeded(curOut, outputVolume);
                            any = true;
                        }
                    }

                    if (commOutputId.HasValue)
                    {
                        var comm = FindDevice(outs!, commOutputId.Value, commOutputName, out bool byName);
                        if (comm == null)
                        {
                            problems.Add("comm output not connected");
                        }
                        else
                        {
                            SetDefaultCommOnlyLocked(DataFlow.Render, comm.RawId);
                            any = true;
                            if (byName) resolvedCommOut = comm.Id;
                        }
                    }

                    if (inputId.HasValue)
                    {
                        var d = FindDevice(ins!, inputId.Value, inputName, out bool byName);
                        if (d == null)
                        {
                            problems.Add("input not connected");
                        }
                        else
                        {
                            SetDefaultLocked(DataFlow.Capture, d.RawId, includeComm: true);
                            ApplyVolumeIfNeeded(d.Device, inputVolume);
                            any = true;
                            if (byName) resolvedIn = d.Id;
                        }
                    }
                    else if (inputVolume.HasValue)
                    {
                        using var curIn = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);
                        if (curIn != null)
                        {
                            ApplyVolumeIfNeeded(curIn, inputVolume);
                            any = true;
                        }
                    }
                }
                finally
                {
                    DisposeEntries(outs);
                    DisposeEntries(ins);
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
                if (IsDisposed) return new CycleResult(null, null);
                var next = CycleLocked(DataFlow.Render, filter);
                if (next == null) return new CycleResult(null, null);
                return new CycleResult(next, TakeSnapshotLocked());
            }
        });

    public Task<CycleResult> CycleInputAsync(IReadOnlyCollection<CycleDeviceEntry> filter) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (IsDisposed) return new CycleResult(null, null);
                var next = CycleLocked(DataFlow.Capture, filter);
                if (next == null) return new CycleResult(null, null);
                return new CycleResult(next, TakeSnapshotLocked());
            }
        });

    private DeviceItem? CycleLocked(DataFlow flow, IReadOnlyCollection<CycleDeviceEntry> filter)
    {
        var all = EnumerateEndpointsLocked(flow);
        try
        {
            string? curRaw = TryGetDefaultRawId(flow, Role.Multimedia);
            Guid? curId = curRaw != null ? ProfileLogic.ParseEndpointGuid(curRaw) : null;
            var pairs = all.Select(d => (d.Id, d.FullName)).ToList();

            int nextIdx = ProfileLogic.SelectNextCycleIndex(pairs, curId, filter);
            if (nextIdx < 0) return null;

            var chosen = all[nextIdx];
            SetDefaultLocked(flow, chosen.RawId, includeComm: true);
            return new DeviceItem(chosen.Id, chosen.FullName);
        }
        finally
        {
            DisposeEntries(all);
        }
    }

    /// <summary>Toggles default microphone mute and returns the new state alongside a fresh snapshot.</summary>
    public Task<MuteToggleResult> ToggleMuteAsync() =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (IsDisposed) return new MuteToggleResult(null, null);
                using var mic = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);
                if (mic == null) return new MuteToggleResult(null, null);
                bool nowMuted = !mic.AudioEndpointVolume.Mute;
                mic.AudioEndpointVolume.Mute = nowMuted;
                Interlocked.Exchange(ref _watchedCaptureMuted, nowMuted ? 1 : 0);
                return new MuteToggleResult(nowMuted, TakeSnapshotLocked());
            }
        });

    /// <summary>Sets default microphone mute explicitly (idempotent) and returns a fresh snapshot.</summary>
    public Task<MuteToggleResult> SetMuteAsync(bool mute) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (IsDisposed) return new MuteToggleResult(null, null);
                using var mic = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);
                if (mic == null) return new MuteToggleResult(null, null);
                if (mic.AudioEndpointVolume.Mute != mute)
                {
                    mic.AudioEndpointVolume.Mute = mute;
                    Interlocked.Exchange(ref _watchedCaptureMuted, mute ? 1 : 0);
                }
                return new MuteToggleResult(mute, TakeSnapshotLocked());
            }
        });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;

        // Tear down CoreAudio COM objects on an MTA thread with a bounded wait so exit never freezes the UI thread.
        var cleanup = Task.Run(() =>
        {
            if (!Monitor.TryEnter(_lock, 300)) return;
            try
            {
                ReleaseCaptureWatchLocked();
                if (_notifRegistered && _enumerator != null)
                {
                    try { _enumerator.UnregisterEndpointNotificationCallback(_notifClient); } catch { }
                    _notifRegistered = false;
                }
                if (_enumerator != null)
                {
                    try { _enumerator.Dispose(); } catch { }
                    _enumerator = null;
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        });
        try { cleanup.Wait(400); } catch { }
    }

    private sealed class NotificationClient(AudioService owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            owner.OnEndpointNotificationLockFree();

        public void OnDeviceAdded(string pwstrDeviceId) =>
            owner.OnEndpointNotificationLockFree();

        public void OnDeviceRemoved(string deviceId) =>
            owner.OnEndpointNotificationLockFree();

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            owner.OnEndpointNotificationLockFree();

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}

/// <summary>COM wrapper for Windows AudioSes PolicyConfigClient to switch default CoreAudio endpoints.</summary>
internal static class PolicyConfigClient
{
    public static void SetDefaultEndpoint(string rawEndpointId, Role role)
    {
        object? comObj = null;
        try
        {
            comObj = new PolicyConfigCoclass();
            int hr = comObj switch
            {
                IPolicyConfig pc => pc.SetDefaultEndpoint(rawEndpointId, (uint)role),
                IPolicyConfigVista pcv => pcv.SetDefaultEndpoint(rawEndpointId, (uint)role),
                _ => unchecked((int)0x80004002) // E_NOINTERFACE
            };
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            if (comObj != null && Marshal.IsComObject(comObj))
                Marshal.ReleaseComObject(comObj);
        }
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigCoclass { }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out int pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.U4)] uint role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bVisible);
    }

    [ComImport]
    [Guid("568B9108-44BF-40B4-9006-86AFE5B5A620")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, out IntPtr ppFormat);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out int pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.U4)] uint role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bVisible);
    }
}

internal readonly record struct UpdateDownloadProgress(
    string Stage,
    long BytesReceived,
    long TotalBytes,
    bool Indeterminate)
{
    public int Percent => TotalBytes > 0
        ? Math.Clamp((int)Math.Round(BytesReceived * 100.0 / TotalBytes), 0, 100)
        : 0;
}

internal readonly record struct StagedUpdate(
    string FilePath,
    string Sha256,
    string Version);

internal static class UpdateService
{
    private static readonly HttpClient ApiHttp = CreateApiClient();
    private static readonly HttpClient DownloadHttp = CreateDownloadClient();
    private static readonly JsonSerializerOptions ManifestJsonOpts = new() { WriteIndented = true };

    public static string UpdatesDir { get; } = Path.Combine(AppLog.DataDir, "updates");
    private static string ManifestPath { get; } = Path.Combine(UpdatesDir, "pending-update.json");

    private static HttpClient CreateApiClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8),
            MaxResponseContentBufferSize = ProfileLogic.MaxUpdateResponseBytes
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AudioProfileSwitcher", AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AudioProfileSwitcher", AppInfo.Version));
        return client;
    }

    public static bool CanSelfUpdate(out string? reason)
    {
        reason = null;
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            reason = "Unable to locate the running executable.";
            return false;
        }

        string fileName = Path.GetFileName(exePath);
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Self-update is only available for packaged single-file Windows builds.";
            return false;
        }

        string? dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir))
        {
            reason = "Unable to determine installation folder.";
            return false;
        }

        if (File.Exists(Path.Combine(dir, "AudioProfileSwitcher.dll")))
        {
            reason = "Development folder build detected — download a release build from GitHub.";
            return false;
        }

        try
        {
            string probe = Path.Combine(dir, $".aps-write-probe-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            reason = "Installed in a protected folder — download manually from GitHub or move the app to a user folder for 1-click updates.";
            return false;
        }
    }

    public static async Task<ReleaseCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        using var resp = await ApiHttp.GetAsync(
            AppInfo.LatestReleaseApiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var finalUri = resp.RequestMessage?.RequestUri;
        if (finalUri == null ||
            !finalUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !finalUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected update server host.");
        }

        var json = await ReadBoundedTextAsync(resp, ProfileLogic.MaxUpdateResponseBytes, ct).ConfigureAwait(false);
        return ProfileLogic.ParseLatestRelease(json, AppInfo.Version);
    }

    public static async Task<StagedUpdate> DownloadAndVerifyUpdateAsync(
        ReleaseCheckResult release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!release.HasVerifiedDownload ||
            string.IsNullOrEmpty(release.AssetName) ||
            string.IsNullOrEmpty(release.AssetDownloadUrl))
        {
            throw new InvalidOperationException("This release does not include a verifiable executable download for this build.");
        }

        // 1. Resolve and cross-check SHA-256 checksum before saving the executable.
        progress?.Report(new UpdateDownloadProgress("Fetching SHA-256 checksum…", 0, release.AssetSizeBytes, Indeterminate: true));
        string? sumsHash = null;
        if (!string.IsNullOrEmpty(release.ChecksumsUrl))
        {
            using var sumsResp = await SendWithValidatedRedirectsAsync(release.ChecksumsUrl, ct).ConfigureAwait(false);
            string sumsText = await ReadBoundedTextAsync(sumsResp, ProfileLogic.MaxChecksumsBytes, ct).ConfigureAwait(false);
            sumsHash = ProfileLogic.ParseSha256Sums(sumsText, release.AssetName);
        }

        string? expectedSha256 = ProfileLogic.ResolveExpectedSha256(release.AssetSha256, sumsHash);
        if (string.IsNullOrEmpty(expectedSha256))
        {
            throw new InvalidOperationException("This release does not provide a verifiable SHA-256 checksum.");
        }

        // 2. Stream the executable into %AppData%\AudioProfileSwitcher\updates\ with strict size bounds.
        Directory.CreateDirectory(UpdatesDir);
        TryCleanUpdatesDirectory(excludePath: null);

        string tmpPath = Path.Combine(UpdatesDir, $"download-{Guid.NewGuid():N}.tmp");
        try
        {
            using var resp = await SendWithValidatedRedirectsAsync(release.AssetDownloadUrl, ct).ConfigureAwait(false);
            long? contentLen = resp.Content.Headers.ContentLength;
            if (contentLen.HasValue && contentLen.Value > ProfileLogic.MaxUpdateBinaryBytes)
                throw new InvalidDataException("Update binary exceeded maximum allowed size.");

            long totalBytes = contentLen is > 0 ? contentLen.Value : release.AssetSizeBytes;
            progress?.Report(new UpdateDownloadProgress($"Downloading {release.AssetName}…", 0, totalBytes, Indeterminate: totalBytes <= 0));

            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                var buffer = new byte[65536];
                long received = 0;
                long lastTick = Environment.TickCount64;

                while (true)
                {
                    int read = await src.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0) break;

                    received += read;
                    if (received > ProfileLogic.MaxUpdateBinaryBytes)
                        throw new InvalidDataException("Update binary exceeded maximum allowed size.");

                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                    long now = Environment.TickCount64;
                    if (now - lastTick >= 75 || (totalBytes > 0 && received >= totalBytes))
                    {
                        lastTick = now;
                        progress?.Report(new UpdateDownloadProgress(
                            $"Downloading {release.AssetName}…",
                            received,
                            totalBytes,
                            Indeterminate: totalBytes <= 0));
                    }
                }

                await dst.FlushAsync(ct).ConfigureAwait(false);

                if (received < ProfileLogic.MinUpdateBinaryBytes)
                    throw new InvalidDataException("Downloaded file is unexpectedly small and may be corrupted.");
            }

            // 3. Re-read staged file from disk to verify Windows PE ("MZ") header and SHA-256 digest.
            progress?.Report(new UpdateDownloadProgress("Verifying SHA-256 integrity…", totalBytes, totalBytes, Indeterminate: true));
            await VerifyFilePeAndSha256Async(tmpPath, expectedSha256, ct).ConfigureAwait(false);

            string safeVer = SanitizeFileToken(release.LatestVersion);
            string stagedPath = Path.Combine(UpdatesDir, $"AudioProfileSwitcher-update-{safeVer}.exe");
            File.Move(tmpPath, stagedPath, overwrite: true);

            AppLog.Info($"Downloaded and verified update {release.LatestVersion} ({release.AssetName}, SHA-256: {expectedSha256[..12]}…).");
            return new StagedUpdate(stagedPath, expectedSha256, release.LatestVersion);
        }
        catch
        {
            TryDeleteFile(tmpPath);
            throw;
        }
    }

    public static bool LaunchStagedUpdater(StagedUpdate staged, bool relaunchMinimized)
    {
        string? targetPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            throw new InvalidOperationException("Unable to locate the running executable.");

        var manifest = new PendingUpdateManifest
        {
            ParentPid = Environment.ProcessId,
            TargetPath = Path.GetFullPath(targetPath),
            StagedPath = Path.GetFullPath(staged.FilePath),
            ExpectedSha256 = staged.Sha256,
            TargetVersion = staged.Version,
            RelaunchMinimized = relaunchMinimized
        };

        Directory.CreateDirectory(UpdatesDir);
        string tmpManifest = ManifestPath + ".tmp";
        File.WriteAllText(tmpManifest, JsonSerializer.Serialize(manifest, ManifestJsonOpts));
        File.Move(tmpManifest, ManifestPath, overwrite: true);

        var psi = new ProcessStartInfo
        {
            FileName = staged.FilePath,
            WorkingDirectory = UpdatesDir,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--apply-update");

        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start staged updater process.");
        proc.Dispose();
        AppLog.Info($"Launched staged updater for {staged.Version}; exiting current instance.");
        return true;
    }

    /// <summary>
    /// Internal one-shot update applier invoked when the verified staged EXE is started with --apply-update.
    /// Strictly refuses to run outside %AppData%\AudioProfileSwitcher\updates\ or without a verified manifest.
    /// </summary>
    public static void RunApplyUpdateMode()
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            AppLog.Warn("Updater aborted: missing current executable path.");
            return;
        }

        if (!ProfileLogic.IsPathStrictlyInsideDirectory(currentExe, UpdatesDir))
        {
            AppLog.Warn("Updater aborted: --apply-update can only run from the app's updates staging directory.");
            return;
        }

        PendingUpdateManifest? manifest = null;
        try
        {
            if (!File.Exists(ManifestPath))
            {
                AppLog.Warn("Updater aborted: pending-update.json not found.");
                return;
            }
            string json = File.ReadAllText(ManifestPath);
            TryDeleteFile(ManifestPath);
            manifest = JsonSerializer.Deserialize<PendingUpdateManifest>(json);
        }
        catch (Exception ex)
        {
            AppLog.Error("Updater aborted: failed to read pending-update.json", ex);
            return;
        }

        if (manifest == null) return;

        string fullCurrent = Path.GetFullPath(currentExe);
        string fullStaged;
        string fullTarget;
        try
        {
            fullStaged = Path.GetFullPath(manifest.StagedPath);
            fullTarget = Path.GetFullPath(manifest.TargetPath);
        }
        catch
        {
            AppLog.Warn("Updater aborted: invalid manifest paths.");
            return;
        }

        if (!fullCurrent.Equals(fullStaged, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Warn("Updater aborted: staged executable path does not match manifest.");
            return;
        }

        if (!fullTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullTarget))
        {
            AppLog.Warn("Updater aborted: target executable does not exist or is invalid.");
            return;
        }

        try
        {
            if ((File.GetAttributes(fullTarget) & FileAttributes.ReparsePoint) != 0)
            {
                AppLog.Warn("Updater aborted: target executable is a symlink/reparse point.");
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Updater aborted: could not inspect target file attributes", ex);
            return;
        }

        string? expectedSha256 = ProfileLogic.NormalizeSha256Hex(manifest.ExpectedSha256);
        if (expectedSha256 == null)
        {
            AppLog.Warn("Updater aborted: missing or invalid expected SHA-256.");
            return;
        }

        try
        {
            VerifyFilePeAndSha256Async(fullCurrent, expectedSha256, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Error("Updater aborted: staged binary failed self-integrity verification", ex);
            return;
        }

        // Wait cleanly for the previous instance to exit and release its file/mutex handles.
        if (manifest.ParentPid > 0 && manifest.ParentPid != Environment.ProcessId)
        {
            try
            {
                using var parent = Process.GetProcessById(manifest.ParentPid);
                parent.WaitForExit(15_000);
            }
            catch { /* parent already exited */ }
        }

        string targetTmp = fullTarget + ".update.tmp";
        Exception? lastError = null;
        bool replaced = false;

        try
        {
            File.Copy(fullCurrent, targetTmp, overwrite: true);
            for (int attempt = 0; attempt < 25; attempt++)
            {
                try
                {
                    File.Move(targetTmp, fullTarget, overwrite: true);
                    replaced = true;
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lastError = ex;
                    Thread.Sleep(250);
                }
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        if (!replaced)
        {
            TryDeleteFile(targetTmp);
            AppLog.Error($"Updater failed to replace '{fullTarget}'", lastError);
            try
            {
                MessageBox.Show(
                    $"AudioSwitcher couldn't finish replacing the executable:\n\n{lastError?.Message ?? "File is locked or access was denied."}\n\nYour existing version was not modified.",
                    "AudioSwitcher Update Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch { }

            RelaunchTarget(fullTarget, updatedVersion: null, minimized: manifest.RelaunchMinimized);
            return;
        }

        string safeVersion = ProfileLogic.SanitizeDisplayString(manifest.TargetVersion, 32, $"v{AppInfo.Version}");
        AppLog.Info($"Updated executable '{fullTarget}' to {safeVersion}. Relaunching…");
        RelaunchTarget(fullTarget, updatedVersion: safeVersion, minimized: manifest.RelaunchMinimized);
    }

    public static void CleanupStagedUpdatesAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!Directory.Exists(UpdatesDir)) return;
                await Task.Delay(1500).ConfigureAwait(false);
                TryCleanUpdatesDirectory(excludePath: null);
                if (Directory.Exists(UpdatesDir) && Directory.EnumerateFileSystemEntries(UpdatesDir).Any())
                {
                    await Task.Delay(2500).ConfigureAwait(false);
                    TryCleanUpdatesDirectory(excludePath: null);
                }
                if (Directory.Exists(UpdatesDir) && !Directory.EnumerateFileSystemEntries(UpdatesDir).Any())
                {
                    Directory.Delete(UpdatesDir);
                }
            }
            catch { /* best-effort cleanup */ }
        });
    }

    private static void RelaunchTarget(string targetPath, string? updatedVersion, bool minimized)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                WorkingDirectory = Path.GetDirectoryName(targetPath) ?? AppLog.DataDir,
                UseShellExecute = false
            };
            if (!string.IsNullOrEmpty(updatedVersion))
            {
                psi.ArgumentList.Add("--updated-to");
                psi.ArgumentList.Add(updatedVersion);
            }
            if (minimized)
            {
                psi.ArgumentList.Add("--minimized");
            }
            using var child = Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to relaunch AudioProfileSwitcher after update", ex);
        }
    }

    private static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(string startUrl, CancellationToken ct)
    {
        const int maxRedirects = 5;
        if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var currentUri) ||
            !ProfileLogic.IsAllowedUpdateDownloadUri(currentUri))
        {
            throw new InvalidOperationException("Blocked download from untrusted host.");
        }

        for (int hop = 0; hop <= maxRedirects; hop++)
        {
            var resp = await DownloadHttp.GetAsync(currentUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            int code = (int)resp.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308 && resp.Headers.Location != null)
            {
                var nextUri = resp.Headers.Location.IsAbsoluteUri
                    ? resp.Headers.Location
                    : new Uri(currentUri, resp.Headers.Location);
                resp.Dispose();

                if (!ProfileLogic.IsAllowedUpdateDownloadUri(nextUri))
                    throw new InvalidOperationException("Blocked update redirect to untrusted host.");

                currentUri = nextUri;
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                using (resp)
                {
                    resp.EnsureSuccessStatusCode();
                }
            }
            return resp;
        }

        throw new InvalidOperationException("Too many redirects while downloading update.");
    }

    private static async Task<string> ReadBoundedTextAsync(HttpResponseMessage resp, int maxBytes, CancellationToken ct)
    {
        if (resp.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("Update response exceeded size limit.");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException("Update response exceeded size limit.");
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task VerifyFilePeAndSha256Async(string filePath, string expectedSha256Hex, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length < ProfileLogic.MinUpdateBinaryBytes || info.Length > ProfileLogic.MaxUpdateBinaryBytes)
            throw new InvalidDataException("Update binary size is outside expected safety bounds.");

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var magic = new byte[2];
        int magicRead = await fs.ReadAsync(magic, ct).ConfigureAwait(false);
        if (magicRead < 2 || magic[0] != (byte)'M' || magic[1] != (byte)'Z')
            throw new InvalidDataException("Downloaded file is not a valid Windows executable.");

        fs.Position = 0;
        byte[] actualHash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        byte[] expectedHash = Convert.FromHexString(expectedSha256Hex);

        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException("Downloaded update failed SHA-256 integrity verification.");
    }

    private static string SanitizeFileToken(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (char ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_') sb.Append(ch);
        }
        return sb.Length > 0 ? sb.ToString() : "latest";
    }

    private static void TryCleanUpdatesDirectory(string? excludePath)
    {
        if (!Directory.Exists(UpdatesDir)) return;
        foreach (var file in Directory.EnumerateFiles(UpdatesDir))
        {
            if (excludePath != null && Path.GetFullPath(file).Equals(Path.GetFullPath(excludePath), StringComparison.OrdinalIgnoreCase))
                continue;
            TryDeleteFile(file);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
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
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeyService()
    {
        CreateHandle(new CreateParams());
    }

    public List<string> RegisterAll(
        IReadOnlyList<(string Name, HotKeyBinding Binding, Action Action)> items,
        bool force = false)
    {
        var enabledItems = items.Where(x => x.Binding.IsEnabled).ToList();

        if (!force && _registeredSpec != null && _lastFailures.Count == 0 && SpecMatches(_registeredSpec, enabledItems))
        {
            for (int i = 0; i < _slotIds.Count; i++)
            {
                _actions[_slotIds[i]] = enabledItems[i].Action;
            }
            return _lastFailures;
        }

        UnregisterAll();
        var spec = new List<(string Name, HotKeyModifiers Modifiers, Keys Key)>(enabledItems.Count);
        var failures = new List<string>();

        foreach (var (name, binding, action) in enabledItems)
        {
            spec.Add((name, binding.Modifiers, binding.Key));
            int id = _nextId++;
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
                await Task.Delay(120).ConfigureAwait(false);
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
                    loaded.Player.Play();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"Chime playback failed ({cue})", ex);
            }
        }
    }
}