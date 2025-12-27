using System.IO;
using NAudio.Wave;

namespace VoiceWin.Services;

public class SoundService : IDisposable
{
    private readonly string _startSoundPath;
    private readonly string _endSoundPath;

    public SoundService()
    {
        var baseDir = AppContext.BaseDirectory;
        _startSoundPath = Path.Combine(baseDir, "Assets", "sound_start.mp3");
        _endSoundPath = Path.Combine(baseDir, "Assets", "sound_end.mp3");
    }

    public void PlayStartSound()
    {
        PlaySound(_startSoundPath);
    }

    public void PlayEndSound()
    {
        PlaySound(_endSoundPath);
    }

    private void PlaySound(string path)
    {
        if (!File.Exists(path))
            return;

        Task.Run(() =>
        {
            try
            {
                using var audioFile = new AudioFileReader(path);
                using var outputDevice = new WaveOutEvent();
                outputDevice.Init(audioFile);
                outputDevice.Play();
                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }
            }
            catch { }
        });
    }

    public void Dispose() { }
}
