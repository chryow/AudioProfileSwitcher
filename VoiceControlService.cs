using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioProfileSwitcher;

/// <summary>
/// Offline Windows voice-command engine powered by System.Speech + NAudio WasapiCapture.
/// Supports pinning to a specific desk/USB microphone (avoiding Bluetooth HFP degradation)
/// or following Windows' default capture device, with dynamic card-name grammar updates.
/// </summary>
internal sealed class VoiceControlService : IDisposable
{
    private const int CooldownMs = 1200;

    private readonly object _gate = new();
    private SpeechRecognitionEngine? _engine;
    private WasapiCapture? _capture;
    private MMDevice? _captureDevice;
    private BlockingAudioStream? _audioStream;
    private Dictionary<string, VoiceCommand> _commandMap = new(StringComparer.OrdinalIgnoreCase);

    private bool _enabled;
    private Guid? _activeDeviceId;
    private string _activeDeviceName = "";
    private string _activeGrammarSig = "";
    private float _minConfidence = 0.78f;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private bool _disposed;

    public event Action<VoiceCommand>? CommandRecognized;
    public event Action<bool, string>? StatusChanged;

    public bool IsListening { get; private set; }
    public string StatusText { get; private set; } = "Disabled";

    public void Configure(
        bool enabled,
        string wakePhrase,
        int confidencePercent,
        Guid? pinnedDeviceId,
        string? pinnedDeviceName,
        IReadOnlyList<AudioCard> cards)
    {
        var cardsCopy = cards.Select(c => new AudioCard { Id = c.Id, Name = c.Name }).ToList();
        _ = Task.Run(() => ConfigureInternal(enabled, wakePhrase, confidencePercent, pinnedDeviceId, pinnedDeviceName, cardsCopy));
    }

    private void ConfigureInternal(
        bool enabled,
        string wakePhrase,
        int confidencePercent,
        Guid? pinnedDeviceId,
        string? pinnedDeviceName,
        List<AudioCard> cards)
    {
        bool statusDirty = false;
        bool listening = false;
        string statusText = "Disabled";

        lock (_gate)
        {
            if (_disposed) return;

            _minConfidence = Math.Clamp(confidencePercent, 50, 95) / 100f;
            var newMap = ProfileLogic.BuildVoiceCommandMap(wakePhrase, cards);
            string newSig = string.Join("\n", newMap.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

            if (!enabled)
            {
                if (_enabled || _engine != null || _capture != null)
                {
                    StopEngineLocked();
                }
                _enabled = false;
                _commandMap = newMap;
                _activeGrammarSig = newSig;
                statusDirty = UpdateStatusLocked(false, "Disabled");
                listening = IsListening;
                statusText = StatusText;
            }
            else
            {
                _enabled = true;
                var resolved = ResolveCaptureDevice(pinnedDeviceId, pinnedDeviceName);
                if (resolved == null)
                {
                    StopEngineLocked();
                    _commandMap = newMap;
                    _activeGrammarSig = newSig;
                    string waitMsg = pinnedDeviceId.HasValue
                        ? $"Waiting for mic ({ProfileLogic.ExtractShortDeviceName(pinnedDeviceName) ?? "selected"})…"
                        : "Waiting for microphone…";
                    statusDirty = UpdateStatusLocked(false, waitMsg);
                    listening = IsListening;
                    statusText = StatusText;
                }
                else
                {
                    var (dev, devId, devName) = resolved.Value;
                    bool sameDevice = _engine != null && _capture != null && _activeDeviceId == devId;
                    if (sameDevice)
                    {
                        dev.Dispose();
                        if (!string.Equals(_activeGrammarSig, newSig, StringComparison.Ordinal))
                        {
                            try
                            {
                                LoadGrammarLocked(_engine!, newMap);
                                _commandMap = newMap;
                                _activeGrammarSig = newSig;
                            }
                            catch (Exception ex)
                            {
                                AppLog.Error("Failed to reload voice grammar", ex);
                            }
                        }
                        statusDirty = UpdateStatusLocked(true, $"Listening on {ProfileLogic.ExtractShortDeviceName(devName) ?? devName}");
                        listening = IsListening;
                        statusText = StatusText;
                    }
                    else
                    {
                        StopEngineLocked();
                        _commandMap = newMap;
                        _activeGrammarSig = newSig;
                        StartEngineLocked(dev, devId, devName, newMap);
                        statusDirty = true;
                        listening = IsListening;
                        statusText = StatusText;
                    }
                }
            }
        }

        if (statusDirty)
        {
            try { StatusChanged?.Invoke(listening, statusText); } catch { }
        }
    }

    private static (MMDevice Device, Guid Id, string Name)? ResolveCaptureDevice(Guid? pinnedId, string? pinnedName)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!pinnedId.HasValue)
            {
                if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                    return null;
                var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                return (def, ProfileLogic.ParseEndpointGuid(def.ID), def.FriendlyName);
            }

            MMDevice? nameFallback = null;
            var col = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            for (int i = 0; i < col.Count; i++)
            {
                var d = col[i];
                try
                {
                    var id = ProfileLogic.ParseEndpointGuid(d.ID);
                    if (id == pinnedId.Value)
                    {
                        nameFallback?.Dispose();
                        return (d, id, d.FriendlyName);
                    }
                    if (nameFallback == null &&
                        ProfileLogic.IsMeaningfulDeviceName(pinnedName) &&
                        d.FriendlyName.Equals(pinnedName, StringComparison.OrdinalIgnoreCase))
                    {
                        nameFallback = d;
                    }
                    else
                    {
                        d.Dispose();
                    }
                }
                catch
                {
                    d.Dispose();
                }
            }

            if (nameFallback != null)
                return (nameFallback, ProfileLogic.ParseEndpointGuid(nameFallback.ID), nameFallback.FriendlyName);

            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to resolve voice capture device", ex);
            return null;
        }
    }

    private void StartEngineLocked(MMDevice device, Guid devId, string devName, Dictionary<string, VoiceCommand> map)
    {
        try
        {
            var info = SelectRecognizer();
            if (info == null)
            {
                device.Dispose();
                UpdateStatusLocked(false, "Install a Windows Speech pack in Settings → Time & Language → Speech");
                return;
            }

            var engine = new SpeechRecognitionEngine(info);
            LoadGrammarLocked(engine, map);

            var stream = new BlockingAudioStream();
            var fmt = new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);
            engine.SetInputToAudioStream(stream, fmt);
            engine.SpeechRecognized += OnSpeechRecognized;

            var capture = new WasapiCapture(device);
            var wf = capture.WaveFormat;
            bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat;
            int rate = wf.SampleRate;
            int channels = wf.Channels;
            int bits = wf.BitsPerSample;

            capture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;
                var pcm = ProfileLogic.ResampleTo16kMonoPcm16(
                    e.Buffer.AsSpan(0, e.BytesRecorded),
                    rate,
                    channels,
                    bits,
                    isFloat);
                if (pcm.Length > 0) stream.WriteSamples(pcm);
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null)
                    AppLog.Warn("Voice capture stopped: " + e.Exception.Message);
            };

            capture.StartRecording();
            engine.RecognizeAsync(RecognizeMode.Multiple);

            _captureDevice = device;
            _capture = capture;
            _audioStream = stream;
            _engine = engine;
            _activeDeviceId = devId;
            _activeDeviceName = devName;

            string shortName = ProfileLogic.ExtractShortDeviceName(devName) ?? devName;
            UpdateStatusLocked(true, $"Listening on {shortName}");
            AppLog.Info($"Voice control started on '{devName}' ({map.Count} phrases, culture={info.Culture.Name}).");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to start voice control", ex);
            try { device.Dispose(); } catch { }
            StopEngineLocked();
            UpdateStatusLocked(false, "Voice unavailable: " + ex.Message);
        }
    }

    private static RecognizerInfo? SelectRecognizer()
    {
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        if (installed == null || installed.Count == 0) return null;

        var uiCulture = CultureInfo.CurrentUICulture;
        return installed.FirstOrDefault(r => r.Culture.Equals(uiCulture))
            ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName.Equals(uiCulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            ?? installed.FirstOrDefault(r => r.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            ?? installed[0];
    }

    private static void LoadGrammarLocked(SpeechRecognitionEngine engine, Dictionary<string, VoiceCommand> map)
    {
        engine.UnloadAllGrammars();
        if (map.Count == 0) return;

        var choices = new Choices(map.Keys.ToArray());
        var gb = new GrammarBuilder(choices) { Culture = engine.RecognizerInfo.Culture };
        engine.LoadGrammar(new Grammar(gb));
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result == null) return;
        float conf = e.Result.Confidence;
        string text = e.Result.Text ?? "";

        VoiceCommand matched;
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            if (conf < _minConfidence) return;

            var now = DateTime.UtcNow;
            if ((now - _lastTriggerUtc).TotalMilliseconds < CooldownMs) return;

            if (!_commandMap.TryGetValue(text, out var cmd)) return;

            _lastTriggerUtc = now;
            matched = cmd with { Phrase = text, Confidence = conf };
        }

        AppLog.Info($"Voice recognized: '{matched.Phrase}' ({matched.Confidence:P0}) -> {matched.Kind}");
        try { CommandRecognized?.Invoke(matched); }
        catch (Exception ex) { AppLog.Error("Voice command handler failed", ex); }
    }

    private bool UpdateStatusLocked(bool listening, string text)
    {
        bool changed = IsListening != listening || !string.Equals(StatusText, text, StringComparison.Ordinal);
        IsListening = listening;
        StatusText = text;
        return changed;
    }

    private void StopEngineLocked()
    {
        if (_capture != null)
        {
            try { _capture.StopRecording(); } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }

        if (_audioStream != null)
        {
            try { _audioStream.Close(); } catch { }
            try { _audioStream.Dispose(); } catch { }
            _audioStream = null;
        }

        if (_engine != null)
        {
            try { _engine.SpeechRecognized -= OnSpeechRecognized; } catch { }
            try { _engine.RecognizeAsyncCancel(); } catch { }
            try { _engine.Dispose(); } catch { }
            _engine = null;
        }

        if (_captureDevice != null)
        {
            try { _captureDevice.Dispose(); } catch { }
            _captureDevice = null;
        }

        _activeDeviceId = null;
        _activeDeviceName = "";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            StopEngineLocked();
        }
    }

    /// <summary>Bounded blocking ring-buffer stream feeding live 16 kHz PCM16 into System.Speech.</summary>
    private sealed class BlockingAudioStream : Stream
    {
        private const int CapacityBytes = 96_000; // ~3 seconds at 16 kHz * 2 bytes
        private readonly byte[] _ring = new byte[CapacityBytes];
        private readonly object _sync = new();
        private readonly AutoResetEvent _dataSignal = new(false);
        private int _head;
        private int _tail;
        private int _count;
        private bool _closed;

        public void WriteSamples(byte[] data)
        {
            if (data.Length == 0) return;
            lock (_sync)
            {
                if (_closed) return;

                int srcOffset = 0;
                int len = data.Length;
                if (len >= CapacityBytes)
                {
                    srcOffset = len - CapacityBytes;
                    len = CapacityBytes;
                    _head = _tail = _count = 0;
                }
                else if (_count + len > CapacityBytes)
                {
                    // Drop oldest bytes to keep real-time latency tight
                    int drop = _count + len - CapacityBytes;
                    _head = (_head + drop) % CapacityBytes;
                    _count -= drop;
                }

                int firstChunk = Math.Min(len, CapacityBytes - _tail);
                Buffer.BlockCopy(data, srcOffset, _ring, _tail, firstChunk);
                int remaining = len - firstChunk;
                if (remaining > 0)
                    Buffer.BlockCopy(data, srcOffset + firstChunk, _ring, 0, remaining);

                _tail = (_tail + len) % CapacityBytes;
                _count += len;
            }
            _dataSignal.Set();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (true)
            {
                lock (_sync)
                {
                    if (_count > 0)
                    {
                        int toRead = Math.Min(count, _count);
                        int firstChunk = Math.Min(toRead, CapacityBytes - _head);
                        Buffer.BlockCopy(_ring, _head, buffer, offset, firstChunk);
                        int remaining = toRead - firstChunk;
                        if (remaining > 0)
                            Buffer.BlockCopy(_ring, 0, buffer, offset + firstChunk, remaining);

                        _head = (_head + toRead) % CapacityBytes;
                        _count -= toRead;
                        return toRead;
                    }

                    if (_closed) return 0;
                }

                _dataSignal.WaitOne(100);
            }
        }

        public override void Close()
        {
            lock (_sync) { _closed = true; }
            _dataSignal.Set();
            base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            lock (_sync) { _closed = true; }
            _dataSignal.Set();
            if (disposing) _dataSignal.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
