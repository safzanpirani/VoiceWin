using System.IO;
using System.Reflection;
using NAudio.Wave;

namespace VoiceWin.Services;

public class SoundService : IDisposable
{
    private const string StartSoundResource = "VoiceWin.Assets.sound_start.mp3";
    private const string EndSoundResource = "VoiceWin.Assets.sound_end.mp3";

    public void PlayStartSound()
    {
        PlayEmbeddedSound(StartSoundResource);
    }

    public void PlayEndSound()
    {
        PlayEmbeddedSound(EndSoundResource);
    }

    private void PlayEmbeddedSound(string resourceName)
    {
        Task.Run(() =>
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    return;

                // Copy to MemoryStream since NAudio needs seekable stream
                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;

                using var audioFile = new Mp3FileReader(memoryStream);
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
