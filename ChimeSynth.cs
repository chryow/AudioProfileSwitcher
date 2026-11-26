namespace AudioProfileSwitcher;

public enum ChimeCue
{
    Switch,  // E5 -> A5
    Muted,   // A5 -> D5
    Unmuted  // E5 -> B5
}

/// <summary>
/// Pure PCM WAV synthesizer for short two-tone feedback cues.
/// Integrates sine phase continuously across tone transitions to eliminate clicks/pops.
/// </summary>
public static class ChimeSynth
{
    public const int SampleRate = 44100;
    public const double DurationSeconds = 0.16;
    public const double Amplitude = 6000.0;

    // Concert pitches (A4 = 440 Hz): D5 = 587.33, E5 = 659.25, A5 = 880.00, B5 = 987.77
    public const double NoteD5 = 587.33;
    public const double NoteE5 = 659.25;
    public const double NoteA5 = 880.00;
    public const double NoteB5 = 987.77;

    public static (double FirstHz, double SecondHz) GetFrequencies(ChimeCue cue) => cue switch
    {
        ChimeCue.Switch => (NoteE5, NoteA5),
        ChimeCue.Muted => (NoteA5, NoteD5),
        ChimeCue.Unmuted => (NoteE5, NoteB5),
        _ => (NoteE5, NoteA5)
    };

    public static short[] SynthesizeSamples(
        double firstHz,
        double secondHz,
        int sampleRate = SampleRate,
        double durationSec = DurationSeconds,
        double amplitude = Amplitude)
    {
        int sampleCount = Math.Max(2, (int)(sampleRate * durationSec));
        int splitSample = sampleCount / 2;
        var samples = new short[sampleCount];

        double phase = 0.0;
        double denom = sampleCount - 1.0;

        for (int i = 0; i < sampleCount; i++)
        {
            double env = Math.Sin(Math.PI * (i / denom));
            double freq = i < splitSample ? firstHz : secondHz;
            double value = Math.Sin(phase) * env * amplitude;
            samples[i] = (short)Math.Clamp((int)Math.Round(value), short.MinValue, short.MaxValue);

            phase += 2.0 * Math.PI * freq / sampleRate;
            if (phase >= 2.0 * Math.PI)
                phase -= 2.0 * Math.PI;
        }

        return samples;
    }

    public static byte[] BuildWav(ChimeCue cue)
    {
        var (firstHz, secondHz) = GetFrequencies(cue);
        var samples = SynthesizeSamples(firstHz, secondHz);
        int dataSize = samples.Length * sizeof(short);

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // Mono
        bw.Write(SampleRate);
        bw.Write(SampleRate * sizeof(short));
        bw.Write((short)sizeof(short));
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            bw.Write(samples[i]);
        }

        return ms.ToArray();
    }
}
