using System.Media;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
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
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _notifClient;
    private readonly object _lock = new();
    private MMDevice? _watchedCaptureDevice;
    private string? _watchedCaptureRawId;
    private bool _watchedCaptureMuted;
    private bool _notifRegistered;
    private bool _disposed;

    /// <summary>Fired (potentially off the UI thread) when endpoints, defaults, states, or mic mute state change.</summary>
    public event Action? StateChanged;

    private sealed record EndpointEntry(Guid Id, string RawId, string FullName, MMDevice Device);

    public AudioService()
    {
        _notifClient = new NotificationClient(this);
        try
        {
            lock (_lock)
            {
                _enumerator.RegisterEndpointNotificationCallback(_notifClient);
                _notifRegistered = true;
                using var curIn = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);
                EnsureCaptureMuteSubscriptionLocked(curIn?.ID);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to subscribe to CoreAudio notifications; falling back to safety polling", ex);
        }
    }

    private void OnEndpointNotification(bool refreshCaptureWatch)
    {
        bool notify = false;
        lock (_lock)
        {
            if (_disposed) return;
            if (refreshCaptureWatch)
            {
                try
                {
                    using var curIn = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);
                    EnsureCaptureMuteSubscriptionLocked(curIn?.ID);
                }
                catch { /* endpoint may still be settling */ }
            }
            notify = true;
        }

        if (notify) RaiseStateChanged();
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

        if (!string.IsNullOrEmpty(captureRawId))
        {
            try
            {
                var dev = _enumerator.GetDevice(captureRawId);
                _watchedCaptureMuted = dev.AudioEndpointVolume.Mute;
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

    private void OnCaptureVolumeNotification(AudioVolumeNotificationData data)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_disposed) return;
            if (data.Muted != _watchedCaptureMuted)
            {
                _watchedCaptureMuted = data.Muted;
                changed = true;
            }
        }

        if (changed) RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { AppLog.Error("AudioService StateChanged handler failed", ex); }
    }

    private MMDevice? TryGetDefaultEndpoint(DataFlow flow, Role role)
    {
        try
        {
            if (!_enumerator.HasDefaultAudioEndpoint(flow, role)) return null;
            return _enumerator.GetDefaultAudioEndpoint(flow, role);
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
        MMDeviceCollection? col = null;
        try
        {
            col = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
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
                if (_disposed) return (new List<DeviceItem>(), new List<DeviceItem>());
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
                    _watchedCaptureMuted = micMuted;
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
                if (_disposed) return null;
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
                if (_disposed) return new ApplyResult(false, "audio service closed");

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
                if (_disposed) return new CycleResult(null, null);
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
                if (_disposed) return new CycleResult(null, null);
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
                if (_disposed) return new MuteToggleResult(null, null);
                using var mic = TryGetDefaultEndpoint(DataFlow.Capture, Role.Multimedia);
                if (mic == null) return new MuteToggleResult(null, null);
                bool nowMuted = !mic.AudioEndpointVolume.Mute;
                mic.AudioEndpointVolume.Mute = nowMuted;
                _watchedCaptureMuted = nowMuted;
                return new MuteToggleResult(nowMuted, TakeSnapshotLocked());
            }
        });

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseCaptureWatchLocked();
            if (_notifRegistered)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_notifClient); } catch { }
                _notifRegistered = false;
            }
            try { _enumerator.Dispose(); } catch { }
        }
    }

    private sealed class NotificationClient(AudioService owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            owner.OnEndpointNotification(refreshCaptureWatch: flow == DataFlow.Capture);

        public void OnDeviceAdded(string pwstrDeviceId) =>
            owner.OnEndpointNotification(refreshCaptureWatch: true);

        public void OnDeviceRemoved(string deviceId) =>
            owner.OnEndpointNotification(refreshCaptureWatch: true);

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            owner.OnEndpointNotification(refreshCaptureWatch: true);

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

internal static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
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

    public static async Task<ReleaseCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(
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

        if (resp.Content.Headers.ContentLength > ProfileLogic.MaxUpdateResponseBytes)
            throw new InvalidDataException("Update metadata response exceeded size limit.");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > ProfileLogic.MaxUpdateResponseBytes)
                throw new InvalidDataException("Update metadata response exceeded size limit.");
            ms.Write(buffer, 0, read);
        }

        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        return ProfileLogic.ParseLatestRelease(json, AppInfo.Version);
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