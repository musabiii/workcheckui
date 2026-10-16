using System.Diagnostics;
using NAudio;
using NAudio.Wave;

namespace WorkCheck.Services;

public enum AmbientSound { White, Pink, Brown }

// Background noise for focus. The noise is generated on the fly, so it has no loop seam and needs no files.
public sealed class AmbientSoundService : IDisposable
{
    private WaveOutEvent? _output;
    private NoiseProvider? _noise;
    private float _volume = 0.5f;

    public AmbientSound? Current { get; private set; }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_noise != null) _noise.Volume = _volume;
        }
    }

    public void Play(AmbientSound sound)
    {
        if (Current == sound) return;
        Stop();

        try
        {
            _noise = new NoiseProvider(sound) { Volume = _volume };
            _output = new WaveOutEvent { DesiredLatency = 200 };
            _output.Init(_noise);
            _output.Play();
            Current = sound;
        }
        catch (MmException ex)
        {
            Debug.WriteLine($"[AmbientSound] Не удалось включить звук: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _noise = null;
        Current = null;
    }

    public void Dispose() => Stop();
}

internal sealed class NoiseProvider(AmbientSound sound) : ISampleProvider
{
    private readonly Random _random = new();
    private float _gain;
    private double _b0, _b1, _b2, _b3, _b4, _b5, _b6;
    private double _brown;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

    // Written from the UI thread, read on the audio thread; a torn read is impossible for a float
    public float Volume { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        // Squared so the slider feels even across its range
        var target = Volume * Volume;

        for (var i = 0; i < count; i++)
        {
            // Glide to the target gain so start and volume changes never click
            _gain += (target - _gain) * 0.0005f;
            buffer[offset + i] = (float)(NextSample() * _gain);
        }
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
