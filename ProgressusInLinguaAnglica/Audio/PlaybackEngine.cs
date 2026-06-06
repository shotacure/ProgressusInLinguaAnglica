using System;
using System.IO;
using NAudio.Wave;

namespace ProgressusInLinguaAnglica.Audio
{
    /// <summary>再生エンジンの状態。</summary>
    public enum EnginePlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    /// <summary>
    /// NAudio を使った PCM16 モノラル再生エンジン。
    /// SoundPlayer と違い、一時停止/再開・停止・末尾到達の検知に対応する。
    /// PCM はメモリ上に展開済みのものを受け取る（抽出・デコードは呼び出し側）。
    /// </summary>
    public sealed class PlaybackEngine : IDisposable
    {
        private readonly WaveFormat _format;
        private WaveOutEvent? _waveOut;
        private RawSourceWaveStream? _stream;
        private bool _stopRequested;
        private bool _disposed;
        private float _volume = 1f;

        /// <summary>現在の再生状態。</summary>
        public EnginePlaybackState State { get; private set; } = EnginePlaybackState.Stopped;

        /// <summary>音量（0.0〜1.0）。再生中なら即時反映する。</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                if (_waveOut is not null) _waveOut.Volume = _volume;
            }
        }

        /// <summary>
        /// セグメントを末尾まで再生しきって自然停止したときだけ発火する。
        /// Stop() / 次の Play() による停止では発火しない。UI スレッドで呼ばれる。
        /// </summary>
        public event EventHandler? PlaybackEnded;

        /// <summary>
        /// 再生エンジンを生成する。
        /// </summary>
        /// <param name="sampleRate">サンプルレート（例: 18900）</param>
        public PlaybackEngine(int sampleRate)
        {
            _format = new WaveFormat(sampleRate, 16, 1);
        }

        /// <summary>
        /// PCM を先頭から再生する。再生中のものがあれば停止してから差し替える。
        /// </summary>
        /// <param name="pcm">PCM16 モノラルサンプル</param>
        public void Play(short[] pcm)
        {
            StopInternal();
            if (_disposed || pcm is null || pcm.Length == 0) return;

            byte[] bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            _stream = new RawSourceWaveStream(new MemoryStream(bytes), _format);

            _waveOut = new WaveOutEvent();
            _waveOut.PlaybackStopped += OnPlaybackStopped;
            _waveOut.Init(_stream);
            _waveOut.Volume = _volume;
            _stopRequested = false;
            _waveOut.Play();
            State = EnginePlaybackState.Playing;
        }

        /// <summary>再生中なら一時停止する。</summary>
        public void Pause()
        {
            if (State == EnginePlaybackState.Playing && _waveOut is not null)
            {
                _waveOut.Pause();
                State = EnginePlaybackState.Paused;
            }
        }

        /// <summary>一時停止中なら再開する。</summary>
        public void Resume()
        {
            if (State == EnginePlaybackState.Paused && _waveOut is not null)
            {
                _waveOut.Play();
                State = EnginePlaybackState.Playing;
            }
        }

        /// <summary>再生を停止する（PlaybackEnded は発火しない）。</summary>
        public void Stop()
        {
            StopInternal();
        }

        private void StopInternal()
        {
            if (_waveOut is not null)
            {
                _stopRequested = true;
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                try { _waveOut.Stop(); } catch { /* 破棄時の競合は無視 */ }
                _waveOut.Dispose();
                _waveOut = null;
            }
            _stream?.Dispose();
            _stream = null;
            State = EnginePlaybackState.Stopped;
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // ここに来るのは末尾まで再生しきった自然停止のみ
            // （Stop 経由は購読解除してから止めている）。
            if (_stopRequested) return;
            State = EnginePlaybackState.Stopped;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopInternal();
        }
    }
}
