using System.IO;
using NAudio.Wave;
using SileroVad;

namespace VoiceWin.Services;

public class VadService : IDisposable
{
    private Vad? _vad;
    private bool _isInitialized;
    private readonly object _lock = new();

    public bool IsInitialized => _isInitialized;

    public bool Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return true;

            try
            {
                _vad = new Vad();
                _isInitialized = true;
                return true;
            }
            catch
            {
                _isInitialized = false;
                return false;
            }
        }
    }

    public byte[] TrimSilence(byte[] wavAudio, float threshold = 0.5f, int minSilenceDurationMs = 500)
    {
        if (!_isInitialized || _vad == null || wavAudio.Length < 1000)
            return wavAudio;

        try
        {
            var samples = ConvertWavBytesToFloats(wavAudio);
            if (samples.Length == 0)
                return wavAudio;

            var speechTimestamps = _vad.GetSpeechTimestamps(
                samples,
                threshold: threshold,
                min_silence_duration_ms: minSilenceDurationMs);

            if (speechTimestamps.Count == 0)
                return Array.Empty<byte>();

            var speechSamples = VadHelper.GetSpeechSamples(samples, speechTimestamps);
            return ConvertFloatsToWavBytes(speechSamples);
        }
        catch
        {
            return wavAudio;
        }
    }

    public bool HasSpeech(byte[] wavAudio, float threshold = 0.5f)
    {
        if (!_isInitialized || _vad == null || wavAudio.Length < 100)
            return true;

        try
        {
            var samples = ConvertWavBytesToFloats(wavAudio);
            if (samples.Length == 0)
                return true;

            var speechTimestamps = _vad.GetSpeechTimestamps(samples, threshold: threshold);
            return speechTimestamps.Count > 0;
        }
        catch
        {
            return true;
        }
    }

    private static float[] ConvertWavBytesToFloats(byte[] wavBytes)
    {
        using var memoryStream = new MemoryStream(wavBytes);
        using var waveReader = new WaveFileReader(memoryStream);

        var sampleProvider = waveReader.ToSampleProvider();
        var sampleCount = (int)(waveReader.SampleCount);
        var samples = new float[sampleCount];
        sampleProvider.Read(samples, 0, sampleCount);

        return samples;
    }

    private static byte[] ConvertFloatsToWavBytes(IEnumerable<float> samples)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new WaveFileWriter(memoryStream, new WaveFormat(16000, 16, 1));

        foreach (var sample in samples)
        {
            writer.WriteSample(sample);
        }

        writer.Flush();
        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        _vad = null;
        _isInitialized = false;
    }
}
