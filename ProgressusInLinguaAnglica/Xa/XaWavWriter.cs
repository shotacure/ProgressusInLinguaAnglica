using System;
using System.IO;
using System.Text;

namespace ProgressusInLinguaAnglica.Xa
{
    public static class XaWavWriter
    {
        /// <summary>
        /// PCM 16 ビットモノラル WAV 書き出し (ファイル)
        /// </summary>
        /// <param name="path">出力ファイルパス</param>
        /// <param name="sampleRate">サンプリングレート</param>
        /// <param name="samples">サンプル列</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WritePcm16MonoWav(string path, int sampleRate, short[] samples)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (samples is null) throw new ArgumentNullException(nameof(samples));

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

            WritePcm16MonoWavCore(bw, sampleRate, samples);
        }

        /// <summary>
        /// PCM 16 ビットモノラル WAV 書き出し (ストリーム)
        /// </summary>
        /// <param name="stream">出力ストリーム</param>
        /// <param name="sampleRate">サンプリングレート</param>
        /// <param name="samples">サンプル列</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static void WritePcm16MonoWav(Stream stream, int sampleRate, short[] samples)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (!stream.CanWrite) throw new ArgumentException("書き込み不可なストリームです。", nameof(stream));

            using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            WritePcm16MonoWavCore(bw, sampleRate, samples);
        }

        /// <summary>
        /// PCM 16 ビットモノラル WAV 書き出し
        /// </summary>
        /// <param name="bw">バイナリライター</param>
        /// <param name="sampleRate">サンプリングレート</param>
        /// <param name="samples">サンプル列</param>
        private static void WritePcm16MonoWavCore(BinaryWriter bw, int sampleRate, short[] samples)
        {
            int channels = 1;
            int bitsPerSample = 16;

            int dataSize = samples.Length * (bitsPerSample / 8);
            int fmtChunkSize = 16;
            int riffSize = 4 + 8 + fmtChunkSize + 8 + dataSize;

            // RIFF ヘッダ
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(riffSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt チャンク
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(fmtChunkSize);
            bw.Write((short)1); // PCM
            bw.Write((short)channels);
            bw.Write(sampleRate);

            int byteRate = sampleRate * channels * bitsPerSample / 8;
            short blockAlign = (short)(channels * bitsPerSample / 8);

            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write((short)bitsPerSample);

            // data チャンク
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);

            // サンプル書き出しループ
            foreach (short s in samples)
            {
                bw.Write(s);
            }
        }
    }
}
