using System;
using System.IO;

namespace ProgressusInLinguaAnglica.Xa
{
    /// <summary>
    /// RIFF CDXA (SOUND.RTF) の data チャンク位置と、セクタ生データを読み出す。
    /// 読み込み時に全セクタを走査せず、ファイルハンドルを開きっぱなしにして
    /// 必要なセクタだけをオンデマンドでシークして取得する。
    /// </summary>
    public sealed class XaRiffReader : IDisposable
    {
        /// <summary>1 セクタの総バイト数（sync + header + subheader + data + EDC）。</summary>
        public const int SectorSize = 2352;

        /// <summary>sync(12) + header(4)。サブヘッダ以降のユーザーデータまでのオフセット。</summary>
        private const int HeaderBytes = 12 + 4;

        /// <summary>subheader(8) + data(2304) + 0埋め(20) + EDC(4)。</summary>
        public const int UserDataSize = 2336;

        private readonly FileStream _fs;
        private readonly object _ioLock = new();
        private bool _disposed;

        public string FilePath { get; }
        public long DataOffset { get; }
        public int DataSize { get; }

        /// <summary>data チャンク内のセクタ総数。</summary>
        public int SectorCount => DataSize / SectorSize;

        /// <summary>先頭セクタの絶対フレーム（MSF を 75fps に換算したもの）。見つからなければ 0。</summary>
        public int BaseFrame { get; }

        /// <summary>
        /// XA RIFF リーダー。RIFF ヘッダだけを読み、本体は開いたまま保持する。
        /// </summary>
        /// <param name="path">SOUND.RTF のファイルパス</param>
        /// <exception cref="ArgumentNullException">ファイルパス指定なし</exception>
        /// <exception cref="InvalidDataException">データ不正</exception>
        public XaRiffReader(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            FilePath = path;

            // セッション中ずっと開きっぱなしにする（セクタごとに開き直さない）。
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 bufferSize: SectorSize * 64, FileOptions.RandomAccess);

            using (var br = new BinaryReader(_fs, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                // RIFF ヘッダをざっくり読む ("RIFF" + size + "CDXA")
                var riffId = br.ReadBytes(4);
                if (riffId.Length < 4 || riffId[0] != 'R' || riffId[1] != 'I' || riffId[2] != 'F' || riffId[3] != 'F')
                    throw new InvalidDataException("RIFF ではありません。");

                br.ReadInt32(); // riffSize（未使用）
                var cdxaId = br.ReadBytes(4);
                if (cdxaId[0] != 'C' || cdxaId[1] != 'D' || cdxaId[2] != 'X' || cdxaId[3] != 'A')
                    throw new InvalidDataException("CDXA ではありません。");

                // "fmt " チャンクを飛ばす（"fmt " + size + attributes(16) = 24 バイト）
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

            // 先頭セクタのヘッダだけ読んで基準フレームを得る（全走査はしない）。
            if (SectorCount > 0 && TryReadSectorHeader(0, out int baseFrame, out _))
            {
                BaseFrame = baseFrame;
            }
        }

        /// <summary>
        /// 指定 ordinal（0 始まりのセクタ番号）のヘッダから、絶対フレームとチャネルだけを読む。
        /// 2352 バイト全部ではなく先頭 18 バイトだけ読む軽量版。
        /// </summary>
        /// <param name="ordinal">セクタ番号（0 始まり）</param>
        /// <param name="totalFrame">絶対フレーム（75fps 換算）</param>
        /// <param name="channel">サブヘッダのチャネル番号</param>
        /// <returns>読み取りに成功したか</returns>
        public bool TryReadSectorHeader(long ordinal, out int totalFrame, out int channel)
        {
            totalFrame = 0;
            channel = -1;
            if (ordinal < 0 || ordinal >= SectorCount) return false;

            Span<byte> head = stackalloc byte[18]; // [12..14] MSF, [16..17] file/ch
            long offset = DataOffset + ordinal * (long)SectorSize;

            lock (_ioLock)
            {
                if (_disposed) return false;
                _fs.Position = offset;
                int read = _fs.Read(head);
                if (read < head.Length) return false;
            }

            int mm = BcdToInt(head[12]);
            int ss = BcdToInt(head[13]);
            int ff = BcdToInt(head[14]);
            totalFrame = (mm * 60 + ss) * 75 + ff;
            channel = head[17];
            return true;
        }

        /// <summary>
        /// 指定 ordinal から最大 count セクタ分を、セクタ単位の生データとしてまとめて読む。
        /// シーケンシャルなバルク読み込みでセクタごとの seek/open を避ける。
        /// </summary>
        /// <param name="startOrdinal">読み込み開始セクタ番号</param>
        /// <param name="buffer">格納先（count * SectorSize 以上の長さが必要）</param>
        /// <param name="count">読みたいセクタ数</param>
        /// <returns>実際に読めたセクタ数</returns>
        public int ReadSectors(long startOrdinal, byte[] buffer, int count)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            if (startOrdinal < 0 || startOrdinal >= SectorCount || count <= 0) return 0;

            int want = (int)Math.Min(count, SectorCount - startOrdinal);
            int wantBytes = want * SectorSize;
            if (buffer.Length < wantBytes)
                throw new ArgumentException("buffer が小さすぎます。", nameof(buffer));

            long offset = DataOffset + startOrdinal * (long)SectorSize;

            lock (_ioLock)
            {
                if (_disposed) return 0;
                _fs.Position = offset;
                int total = 0;
                while (total < wantBytes)
                {
                    int read = _fs.Read(buffer, total, wantBytes - total);
                    if (read <= 0) break;
                    total += read;
                }
                return total / SectorSize;
            }
        }

        /// <summary>
        /// BCD を int に変換する。
        /// </summary>
        private static int BcdToInt(byte b)
        {
            int hi = (b >> 4) & 0xF;
            int lo = b & 0xF;
            return hi * 10 + lo;
        }

        public void Dispose()
        {
            lock (_ioLock)
            {
                if (_disposed) return;
                _disposed = true;
                _fs.Dispose();
            }
        }
    }
}
