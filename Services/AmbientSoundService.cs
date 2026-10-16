using System.Diagnostics;
using System.IO;
using NAudio;
using NAudio.Vorbis;
using NAudio.Wave;

namespace WorkCheck.Services;

public enum AmbientSound { White, Pink, Brown, ServerRoom, Office, Computer, Cyberpunk, RainThunder, DistantThunder }

// Background sound for focus. Noises are generated on the fly; recordings from files/audio/ambient
// are looped with a crossfade, so neither has an audible seam.
public sealed class AmbientSoundService : IDisposable
{
    private static readonly string RecordingsDir = Path.Combine(AppContext.BaseDirectory, "files", "audio", "ambient");

    // Level multipliers bring each recording close to the noises' loudness;
    // CutEnd drops a silent fade-out at the end so the loop has no gap
    private static readonly Dictionary<AmbientSound, (string File, float Level, double CutEnd)> Recordings = new()
    {
        [AmbientSound.ServerRoom] = ("server-room.ogg", 3f, 0),
        [AmbientSound.Office] = ("office.ogg", 5f, 0.2),
        [AmbientSound.Computer] = ("computer.ogg", 40f, 0),
        [AmbientSound.Cyberpunk] = ("cyberpunk.ogg", 1.2f, 1.5),
        [AmbientSound.RainThunder] = ("rain-thunder.ogg", 5f, 0),
        [AmbientSound.DistantThunder] = ("distant-thunder.ogg", 2.2f, 0)
    };

    private WaveOutEvent? _output;
    private GainProvider? _gain;
    private IDisposable? _source;
    private float _volume = 0.5f;

    public AmbientSound? Current { get; private set; }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_gain != null) _gain.Volume = _volume;
        }
    }

    public void Play(AmbientSound sound)
    {
        if (Current == sound) return;
        Stop();

        try
        {
            ISampleProvider source = Recordings.TryGetValue(sound, out var recording)
                ? new CrossfadeLoopProvider(Path.Combine(RecordingsDir, recording.File), recording.Level, recording.CutEnd)
                : new NoiseProvider(sound);
            _source = source as IDisposable;
            _gain = new GainProvider(source) { Volume = _volume };
            _output = new WaveOutEvent { DesiredLatency = 200 };
            _output.Init(_gain);
            _output.Play();
            Current = sound;
        }
        catch (Exception ex) when (ex is MmException or IOException or InvalidDataException)
        {
            Debug.WriteLine($"[AmbientSound] Не удалось включить {sound}: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _source?.Dispose();
        _source = null;
        _gain = null;
        Current = null;
    }

    public void Dispose() => Stop();
}

// Glides to the target volume so starts and slider moves never click
internal sealed class GainProvider(ISampleProvider source) : ISampleProvider
{
    private float _gain;

    public WaveFormat WaveFormat => source.WaveFormat;

    // Written from the UI thread, read on the audio thread; a torn read is impossible for a float
    public float Volume { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);

        // Squared so the slider feels even across its range
        var target = Volume * Volume;
        for (var i = 0; i < read; i++)
        {
            _gain += (target - _gain) * 0.0005f;
            buffer[offset + i] = SoftLimit(buffer[offset + i] * _gain);
        }
        return read;
    }

    // Leaves normal levels untouched and bends peaks above 0.9 (thunder at full volume) instead of clipping
    private static float SoftLimit(float x)
    {
        var a = MathF.Abs(x);
        if (a <= 0.9f) return x;
        return MathF.CopySign(0.9f + 0.1f * MathF.Tanh((a - 0.9f) / 0.1f), x);
    }
}

internal sealed class NoiseProvider(AmbientSound sound) : ISampleProvider
{
    private readonly Random _random = new();
    private double _b0, _b1, _b2, _b3, _b4, _b5, _b6;
    private double _brown;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

    public int Read(float[] buffer, int offset, int count)
    {
        for (var i = 0; i < count; i++)
            buffer[offset + i] = (float)NextSample();
        return count;
    }

    private double NextSample()
    {
        var white = _random.NextDouble() * 2 - 1;

        switch (sound)
        {
            case AmbientSound.Pink:
                // Paul Kellet's pink noise filter
                _b0 = 0.99886 * _b0 + white * 0.0555179;
                _b1 = 0.99332 * _b1 + white * 0.0750759;
                _b2 = 0.96900 * _b2 + white * 0.1538520;
                _b3 = 0.86650 * _b3 + white * 0.3104856;
                _b4 = 0.55000 * _b4 + white * 0.5329522;
                _b5 = -0.7616 * _b5 - white * 0.0168980;
                var pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362;
                _b6 = white * 0.115926;
                return pink * 0.11;

            case AmbientSound.Brown:
                // Leaky integration of white noise
                _brown = (_brown + 0.02 * white) / 1.02;
                return _brown * 3.5;

            default:
                return white * 0.3;
        }
    }
}

// Plays a recording endlessly. The last seconds fade out while a second reader fades in from the start,
// so the loop point is a smooth blend instead of a cut.
internal sealed class CrossfadeLoopProvider : ISampleProvider, IDisposable
{
    private const double FadeSeconds = 3;

    private readonly string _path;
    private readonly float _level;
    private readonly long _total;
    private readonly long _fade;
    private VorbisWaveReader _current;
    private VorbisWaveReader? _next;
    private long _played;
    private float[] _mix = [];

    public CrossfadeLoopProvider(string path, float level = 1f, double cutEndSeconds = 0)
    {
        _path = path;
        _level = level;
        _current = new VorbisWaveReader(path);
        WaveFormat = _current.WaveFormat;

        // Positions are counted in interleaved samples
        var channels = WaveFormat.Channels;
        var frames = (long)((_current.TotalTime.TotalSeconds - cutEndSeconds) * WaveFormat.SampleRate);
        _total = frames * channels;
        _fade = Math.Min((long)(FadeSeconds * WaveFormat.SampleRate), frames / 4) * channels;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        FillLoop(buffer, offset, count);
        for (var i = 0; i < count; i++)
            buffer[offset + i] *= _level;
        return count;
    }

    private void FillLoop(float[] buffer, int offset, int count)
    {
        var written = 0;
        while (written < count)
        {
            var fadeStart = _total - _fade;

            if (_played < fadeStart)
            {
                var wanted = (int)Math.Min(count - written, fadeStart - _played);
                var read = _current.Read(buffer, offset + written, wanted);
                if (read == 0)
                {
                    // The file ended earlier than its header said: restart without a blend
                    _current.Position = 0;
                    _played = 0;
                    continue;
                }
                _played += read;
                written += read;
                continue;
            }

            var left = (int)Math.Min(count - written, _total - _played);
            if (left <= 0)
            {
                // The fade is over: the second reader, already past its fade-in, becomes the main one
                _current.Dispose();
                _current = _next ?? new VorbisWaveReader(_path);
                _next = null;
                _played = _fade;
                continue;
            }

            _next ??= new VorbisWaveReader(_path);
            if (_mix.Length < left) _mix = new float[left];

            var a = _current.Read(buffer, offset + written, left);
            Array.Clear(buffer, offset + written + a, left - a);
            var b = _next.Read(_mix, 0, left);
            Array.Clear(_mix, b, left - b);

            for (var i = 0; i < left; i++)
            {
                // Equal-power curves keep the loudness steady for uncorrelated ambience
                var t = (_played - fadeStart + i) / (double)_fade * (Math.PI / 2);
                buffer[offset + written + i] = (float)(buffer[offset + written + i] * Math.Cos(t) + _mix[i] * Math.Sin(t));
            }
            _played += left;
            written += left;
        }
    }

    public void Dispose()
    {
        _current.Dispose();
        _next?.Dispose();
    }
}
