using System.IO;
using NAudio.Wave;

namespace VoiceWin.Services;

public class AudioRecordingService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioStream;
    private WaveFileWriter? _waveWriter;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingStopped;

    public void StartRecording()
    {
        if (_isRecording) return;

        _audioStream = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1)
        };

        _waveWriter = new WaveFileWriter(_audioStream, _waveIn.WaveFormat);

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        _waveIn.StartRecording();
        _isRecording = true;
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    public byte[] StopRecording()
    {
        if (!_isRecording || _waveIn == null)
            return Array.Empty<byte>();

        _waveIn.StopRecording();
        _isRecording = false;

        _waveWriter?.Flush();
        _waveWriter?.Dispose();
        _waveWriter = null;

        var audioData = _audioStream?.ToArray() ?? Array.Empty<byte>();

        _audioStream?.Dispose();
        _audioStream = null;

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
        _waveIn = null;

        RecordingStopped?.Invoke(this, EventArgs.Empty);
        return audioData;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            System.Diagnostics.Debug.WriteLine($"Recording error: {e.Exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_isRecording)
            StopRecording();

        _waveIn?.Dispose();
        _waveWriter?.Dispose();
        _audioStream?.Dispose();
    }
}
