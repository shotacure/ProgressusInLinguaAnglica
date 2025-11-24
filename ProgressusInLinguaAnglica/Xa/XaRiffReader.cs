using System;
using System.IO;

namespace ProgressusInLinguaAnglica.Xa
{
    /// <summary>
    /// RIFF CDXA (SOUND.RTF) の data チャンク位置とセクタ生データを読み出す。
    /// </summary>
    public sealed class XaRiffReader
    {
        private const int SectorSize = 2352;

        public string FilePath { get; }
        public long DataOffset { get; }
        public int DataSize { get; }
        public int SectorCount => DataSize / SectorSize;

        public XaRiffReader(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            FilePath = path;

            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            // RIFF ヘッダをざっくり読む ("RIFF" + size + "CDXA")
            var riffId = br.ReadBytes(4);
            if (riffId[0] != 'R' || riffId[1] != 'I' || riffId[2] != 'F' || riffId[3] != 'F')
                throw new InvalidDataException("RIFF ではありません。");

            int riffSize = br.ReadInt32(); // ここでは特に使わない
            var cdxaId = br.ReadBytes(4);
            if (cdxaId[0] != 'C' || cdxaId[1] != 'D' || cdxaId[2] != 'X' || cdxaId[3] != 'A')
                throw new InvalidDataException("CDXA ではありません。");

            // "fmt " チャンクを飛ばす
            // "fmt " + size + attributes(16バイト) までで 4 + 4 + 16 = 24 バイト
            var fmtId = br.ReadBytes(4);
            if (fmtId[0] != 'f' || fmtId[1] != 'm' || fmtId[2] != 't' || fmtId[3] != ' ')
                throw new InvalidDataException("fmt チャンクが見つかりません。");

            int fmtSize = br.ReadInt32();
            if (fmtSize != 16)
                throw new InvalidDataException("fmt チャンクサイズが 16 ではありません。");

            br.ReadBytes(16); // 属性 16 バイトを読み飛ばす

            // "data" チャンク
            var dataId = br.ReadBytes(4);
            if (dataId[0] != 'd' || dataId[1] != 'a' || dataId[2] != 't' || dataId[3] != 'a')
                throw new InvalidDataException("data チャンクが見つかりません。");

            int dataSize = br.ReadInt32();
            DataOffset = br.BaseStream.Position;
            DataSize = dataSize;
        }

        /// <summary>
        /// 指定したセクタ先頭位置から、「サブヘッダ + XA データ 2336 バイト」を読み出す。
        /// sectorFileOffset は 2352バイトセクタの先頭を指している想定。
        /// </summary>
        public byte[] ReadUserData(long sectorFileOffset)
        {
            // sectorFileOffset は「セクタ先頭のファイル位置」
            //
            // SOUND.RTF 1セクタの構造
            //   12バイト : sync
            //   4バイト  : header (mm,ss,ff,mode2)
            //   8バイト  : subheader (file,ch,submode,coding) + コピー
            //   2304バイト: XA ADPCM データ (128byte×18グループ)
            //   20バイト : 0 埋め
            //   4バイト  : EDC
            //
            // ここではヘッダ 16バイト (sync+header) を飛ばし、subheader〜末尾までの 2336 バイトを返す。

            const int headerBytes = 12 + 4;   // sync + header
            const int userDataSize = 2336;    // subheader(8) + 2304 + 20 + 4

            byte[] buf = new byte[userDataSize];
            using var fs = File.OpenRead(FilePath);
            fs.Position = sectorFileOffset + headerBytes;
            int read = fs.Read(buf, 0, userDataSize);
            if (read != userDataSize)
                throw new EndOfStreamException("ユーザーデータ読み込み失敗。");
            return buf;
        }
    }
}
