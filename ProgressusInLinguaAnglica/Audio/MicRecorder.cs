using System;
using System.IO;
using NAudio.Wave;

namespace ProgressusInLinguaAnglica.Audio
{
    /// <summary>
    /// マイク録音と、その録音内容の再生。実機の REC（押している間だけ最大16秒録音）と
    /// PB（録音内容の再生）に相当する。マイクが無い等で失敗しても黙って無効化する。
    /// </summary>
    public sealed class MicRecorder : IDisposable
    {
        private const int MaxSeconds = 16;

        private readonly WaveFormat _format = new(44100, 16, 1);
        private readonly object _lock = new();

        private WaveInEvent? _waveIn;
        private MemoryStream? _buffer;
        private byte[]? _recorded;
        private int _maxBytes;

        private WaveOutEvent? _player;
        private RawSourceWaveStream? _playStream;

        private bool _disposed;

        /// <summary>録音中か。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>再生可能な録音が存在するか。</summary>
        public bool HasRecording
        {
            get { lock (_lock) { return _recorded is { Length: > 0 }; } }
        }

        /// <summary>録音を開始する（最大16秒）。失敗時は何もしない。</summary>
        public void StartRecording()
        {
            if (_disposed) return;
            StopPlayback();
            StopRecording();

            try
            {
                _buffer = new MemoryStream();
                _maxBytes = _format.AverageBytesPerSecond * MaxSeconds;
                _waveIn = new WaveInEvent { WaveFormat = _format, BufferMilliseconds = 50 };
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsRecording = true;
            }
            catch
            {
                // マイク無し等。録音機能を無効化する。
                IsRecording = false;
                _waveIn?.Dispose();
                _waveIn = null;
                _buffer = null;
            }
        }

        /// <summary>録音を停止する。</summary>
        public void StopRecording()
        {
            if (_waveIn is not null && IsRecording)
            {
                try { _waveIn.StopRecording(); } catch { /* 競合は無視 */ }
            }
            IsRecording = false;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (_lock)
            {
                if (_buffer is null) return;
                int remaining = _maxBytes - (int)_buffer.Length;
                if (remaining <= 0) { StopRecording(); return; }
                int n = Math.Min(remaining, e.BytesRecorded);
                _buffer.Write(e.Buffer, 0, n);
                if (_buffer.Length >= _maxBytes) StopRecording();
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            lock (_lock)
            {
                _recorded = _buffer?.ToArray();
                _buffer = null;
            }
            _waveIn?.Dispose();
            _waveIn = null;
        }

        /// <summary>録音内容を再生する。録音が無ければ何もしない。</summary>
        public void PlayRecording()
        {
            if (_disposed) return;
            byte[]? data;
            lock (_lock) { data = _recorded; }
            if (data is null || data.Length == 0) return;

            StopPlayback();
            try
            {
                _playStream = new RawSourceWaveStream(new MemoryStream(data), _format);
                _player = new WaveOutEvent();
                _player.Init(_playStream);
                _player.Play();
            }
            catch
            {
                StopPlayback();
            }
        }

        /// <summary>録音内容の再生を停止する。</summary>
        public void StopPlayback()
        {
            if (_player is not null)
            {
                try { _player.Stop(); } catch { }
                _player.Dispose();
                _player = null;
            }
            _playStream?.Dispose();
            _playStream = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopRecording();
            StopPlayback();
            _waveIn?.Dispose();
            _waveIn = null;
        }
    }
}
