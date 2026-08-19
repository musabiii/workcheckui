using System.Diagnostics;
using System.IO;
using System.Media;

namespace WorkCheck.Services;

public class SoundService
{
    private bool _enabled = true;
    private SoundPlayer? _soundPlayer;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    private SoundPlayer GetSoundPlayer()
    {
        _soundPlayer ??= new SoundPlayer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "files", "audio", "industrial-alarm.wav"));
        return _soundPlayer;
    }

    public void PlayBreakEnd()
    {
        if (!_enabled) return;

        try
        {
            GetSoundPlayer().PlaySync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundService] Error playing sound: {ex.Message}");
            SystemSounds.Exclamation.Play();
        }
    }
}
