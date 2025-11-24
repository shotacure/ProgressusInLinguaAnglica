using System;
using System.Collections.Generic;

namespace ProgressusInLinguaAnglica.Xa
{
    /// <summary>
    /// CD-ROM XA ADPCM (4bit) を PCM16 モノラルにデコードする。
    /// 入力は「subheader(8) + 2304byte データ + 20 + 4」の 2336バイトセクタを
    /// 連結したもの（XaRiffReader.ReadUserData の出力を連結したもの）を想定。
    /// </summary>
    public static class XaAdpcmDecoder
    {
        // XA ADPCM のフィルタ係数テーブル
        private static readonly int[] PosTable = { 0, 60, 115, 98, 122 };
        private static readonly int[] NegTable = { 0, 0, -52, -55, -60 };

        private const int SectorSize = 2336;
        private const int XaSubHeaderSize = 8;          // file, channel, submode, coding + コピー
        private const int XaPortionSize = 128;          // 1 ポーション（ヘッダ16 + データ112）
        private const int XaPortionsPerSector = 0x12;   // 18
        private const int SamplesPerPortionPerChannel = 224; // 4ブロック × 2ニブル × 28サンプル

        /// <summary>
        /// XA ADPCM 生データ（2336 バイトセクタを連結したもの）を PCM16 モノラルに展開する。
        /// </summary>
        /// <param name="xaData">
        /// XaRiffReader.ReadUserData で取得した 2336 バイトの配列を
        /// 複数セクタぶん連結したもの。
        /// </param>
        /// <param name="sampleRate">
        /// サンプルレート指定（例: 37800）。ここでは値は保存せず、主にインターフェイス維持用。
        /// </param>
        public static short[] DecodeMono(byte[] xaData, int sampleRate)
        {
            if (xaData is null) throw new ArgumentNullException(nameof(xaData));

            var samples = new List<short>(xaData.Length * 2);

            int old = 0;
            int older = 0;

            int offset = 0;
            while (offset + SectorSize <= xaData.Length)
            {
                DecodeXaSector2336(xaData, offset, samples, ref old, ref older);
                offset += SectorSize;
            }

            return samples.ToArray();
        }

        /// <summary>
        /// 2336 バイト XA セクタを 1 つデコードして、PCM を samples に追記する（モノラル専用）。
        /// </summary>
        /// <param name="sector"></param>
        /// <param name="sectorOffset"></param>
        /// <param name="samples"></param>
        /// <param name="old"></param>
        /// <param name="older"></param>
        private static void DecodeXaSector2336(
            byte[] sector,
            int sectorOffset,
            List<short> samples,
            ref int old,
            ref int older)
        {
            // sector[0..3] : Subheader (fileNo, channelNo, subMode, codingInfo)
            // sector[4..7] : Subheader copy
            byte fileNo = sector[sectorOffset + 0];
            byte channelNo = sector[sectorOffset + 1];
            byte subMode = sector[sectorOffset + 2];
            byte codingInfo = sector[sectorOffset + 3];

            bool isAudio = (subMode & 0x04) != 0;
            bool isForm2 = (subMode & 0x20) != 0;

            if (!isAudio || !isForm2)
            {
                // 音声でも Form2 でもない → このセクタはスキップ
                return;
            }

            // CodingInfo 解析
            int monoStereoBits = codingInfo & 0x03;
            bool isStereo = monoStereoBits == 1;
            bool isMono = monoStereoBits == 0;

            bool is18900Hz = (codingInfo & 0x04) != 0; // bit2
            // ここでは sampleRate は外からもらうので、coding からのサンプルレートは参照しない

            int bitsCode = (codingInfo >> 4) & 0x03;
            bool is4Bit = bitsCode == 0;
            bool is8Bit = bitsCode == 1;

            if (!is4Bit)
            {
                // 8bit XA-ADPCM は今回は非対応（ノイズ防止のためスキップ）
                return;
            }

            if (!isMono)
            {
                // ステレオ セクタはこのバージョンではスキップ（必要になったら L/R ミックス等を実装）
                return;
            }

            int offset = sectorOffset + XaSubHeaderSize;

            // Mono: 224 サンプル/portion × 18 portions = 4032 サンプル/セクタ
            Span<short> mono = stackalloc short[SamplesPerPortionPerChannel];

            for (int portion = 0; portion < XaPortionsPerSector; portion++)
            {
                int portionOffset = offset + portion * XaPortionSize;
                int dstIndex = 0;
                int localOld = old;
                int localOlder = older;

                for (int blk = 0; blk < 4; blk++)
                {
                    Decode28Nibbles(sector, portionOffset, blk, 0, mono, ref dstIndex, ref localOld, ref localOlder);
                    Decode28Nibbles(sector, portionOffset, blk, 1, mono, ref dstIndex, ref localOld, ref localOlder);
                }

                // セクタをまたぐときも予測フィルタが繋がるように、state を更新
                old = localOld;
                older = localOlder;

                for (int i = 0; i < dstIndex; i++)
                {
                    samples.Add(mono[i]);
                }
            }
        }

        /// <summary>
        /// 1 ポーション内の 28 サンプル (1 ブロック・1 ニブル分) をデコード。
        /// </summary>
        /// <param name="sector"></param>
        /// <param name="portionOffset"></param>
        /// <param name="blk"></param>
        /// <param name="nibble"></param>
        /// <param name="dst"></param>
        /// <param name="dstIndex"></param>
        /// <param name="old"></param>
        /// <param name="older"></param>
        private static void Decode28Nibbles(
            byte[] sector,
            int portionOffset,
            int blk,
            int nibble, // 0 = LO, 1 = HI
            Span<short> dst,
            ref int dstIndex,
            ref int old,
            ref int older)
        {
            // ヘッダバイト：portionOffset + 4 + blk*2 + nibble
            byte header = sector[portionOffset + 4 + blk * 2 + nibble];

            int shift = 12 - (header & 0x0F);
            if (shift < 0) shift = 0; // 保険

            int filter = (header & 0x30) >> 4;
            if (filter < 0 || filter >= PosTable.Length)
            {
                filter = 0;
            }

            int f0 = PosTable[filter];
            int f1 = NegTable[filter];

            for (int j = 0; j < 28; j++)
            {
                // データワード：portionOffset + 16 + blk + j*4
                byte data = sector[portionOffset + 16 + blk + j * 4];
                int nib;

                if (nibble == 0)
                {
                    nib = data & 0x0F; // LO
                }
                else
                {
                    nib = (data >> 4) & 0x0F; // HI
                }

                // 4bit 符号拡張 (-8..+7)
                if ((nib & 0x08) != 0)
                    nib |= unchecked((int)0xFFFFFFF0);

                int sample = (nib << shift) + ((old * f0 + older * f1 + 32) >> 6);

                // -32768..+32767 にクリップ
                if (sample < short.MinValue) sample = short.MinValue;
                if (sample > short.MaxValue) sample = short.MaxValue;

                dst[dstIndex++] = (short)sample;
                older = old;
                old = sample;
            }
        }
    }
}
