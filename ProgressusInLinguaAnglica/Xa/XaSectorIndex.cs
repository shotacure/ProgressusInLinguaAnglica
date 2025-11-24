using System;
using System.Collections.Generic;
using System.IO;

namespace ProgressusInLinguaAnglica.Xa
{
    public sealed class XaSectorIndex
    {
        public sealed class SectorInfo
        {
            public long SectorIndex { get; init; }
            public int Minute { get; init; }
            public int Second { get; init; }
            public int Frame { get; init; }
            public int TotalFrame { get; init; }
            public int Channel { get; init; }
            public long FileOffset { get; init; }
        }

        private readonly List<SectorInfo> _sectors = new();
        private readonly Dictionary<int, List<SectorInfo>> _byChannel = new();

        public XaSectorIndex(XaRiffReader riff)
        {
            BuildIndex(riff);
        }

        private void BuildIndex(XaRiffReader riff)
        {
            const int sectorSize = 2352;
            const int syncHeaderSize = 12 + 4; // sync + header
            const int subHeaderOffset = syncHeaderSize; // 16〜19: file, ch, submode, coding

            using var fs = File.OpenRead(riff.FilePath);
            long pos = riff.DataOffset;
            long end = riff.DataOffset + riff.DataSize;
            long sectorIndex = 0;

            var buf = new byte[sectorSize];

            while (pos + sectorSize <= end)
            {
                fs.Position = pos;
                int read = fs.Read(buf, 0, sectorSize);
                if (read != sectorSize) break;

                // header 部分は BCD mm:ss:ff, mode
                byte mmBcd = buf[12];
                byte ssBcd = buf[13];
                byte ffBcd = buf[14];
                byte mode = buf[15];

                int mm = BcdToInt(mmBcd);
                int ss = BcdToInt(ssBcd);
                int ff = BcdToInt(ffBcd);
                int totalFrames = (mm * 60 + ss) * 75 + ff;

                byte fileNo = buf[subHeaderOffset];
                byte ch = buf[subHeaderOffset + 1];
                byte submode = buf[subHeaderOffset + 2];
                byte coding = buf[subHeaderOffset + 3];

                var info = new SectorInfo
                {
                    SectorIndex = sectorIndex,
                    Minute = mm,
                    Second = ss,
                    Frame = ff,
                    TotalFrame = totalFrames,
                    Channel = ch,
                    FileOffset = pos
                };

                _sectors.Add(info);
                if (!_byChannel.TryGetValue(ch, out var list))
                {
                    list = new List<SectorInfo>();
                    _byChannel[ch] = list;
                }
                list.Add(info);

                sectorIndex++;
                pos += sectorSize;
            }

            // 各チャネルごとに time でソートしておく
            foreach (var kv in _byChannel)
            {
                kv.Value.Sort((a, b) => a.TotalFrame.CompareTo(b.TotalFrame));
            }
        }

        private static int BcdToInt(byte b)
        {
            int hi = (b >> 4) & 0xF;
            int lo = b & 0xF;
            return hi * 10 + lo;
        }

        /// <summary>
        /// 指定チャネルかつフレーム範囲に含まれるセクタ一覧。
        /// </summary>
        public IEnumerable<SectorInfo> GetSectors(int channel, int startFrame, int endFrame)
        {
            if (!_byChannel.TryGetValue(channel, out var list))
                yield break;

            foreach (var s in list)
            {
                if (s.TotalFrame < startFrame) continue;
                if (s.TotalFrame > endFrame) break;
                yield return s;
            }
        }
    }
}
