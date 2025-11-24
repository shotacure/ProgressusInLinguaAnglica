using System;
using System.Collections.Generic;
using System.IO;

namespace ProgressusInLinguaAnglica.Model
{
    public static class TblParser
    {
        /// <summary>
        /// TBL ファイルをパースして TrackTable に変換する。
        /// TRCK / INDX / SUB0 のフォーマット仕様に沿って、
        /// インデックスとサブインデックスの時間情報を抽出する。
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static TrackTable? Parse(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 0x20) return null;

            // --- TRCK: チャプターヘッダ ---
            if (!MatchTag(data, 0, "TRCK"))
                return null;

            var header = ParseTrackHeader(data);

            var table = new TrackTable
            {
                FileName = Path.GetFileName(path),
                Header = header,
                RawData = data,
            };

            // --- INDX を探す（通常は 0x10 にある） ---
            int indxPos = FindChunk(data, "INDX", 0x10);
            if (indxPos < 0)
            {
                // INDX が無いチャプターというのはほぼ無いはずだが、一応ヘッダだけ返す
                return table;
            }

            // INDX チャンクを仕様通りにパース（階層構造＋フラット Segment 双方を構築）
            ParseIndxAndSubChunks(data, indxPos, table);

            if (table.Segments.Count > 0 && table.MainSegment is null)
            {
                table.MainSegment = table.Segments[0];
            }

            return table;
        }

        //=====================================================================
        //  TRCK
        //=====================================================================

        /// <summary>
        /// トラックヘッダのパース
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static TrackHeader ParseTrackHeader(byte[] data)
        {
            // TRCK:
            // [0-3]  'T' 'R' 'C' 'K'
            // [4-7]  予約 (00 00 00 00)
            // [8-9]  trackLength (BE16)
            // [10-11]fileNo      (BE16)
            // [12]   channel
            // [13]   indexLength
            // [14-15]headerSize  (BE16)
            int trackLength = ReadBe16(data, 0x08);
            int fileNo = ReadBe16(data, 0x0A);
            int channel = data[0x0C];
            int indexLength = data[0x0D];
            int headerSize = ReadBe16(data, 0x0E);

            return new TrackHeader
            {
                Size = trackLength,
                FileNo = fileNo,
                Channel = channel,
                IndexLength = indexLength,
                HeaderSize = headerSize
            };
        }

        //=====================================================================
        //  INDX ＋ SUB0
        //=====================================================================

        /// <summary>
        /// INDX チャンクと、そこから参照される SUB0 チャンクをパースする。
        /// ここで TrackIndex / TrackSubIndex の階層構造と、Segment のフラットビューを両方作る。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="indxPos"></param>
        /// <param name="table"></param>
        private static void ParseIndxAndSubChunks(byte[] data, int indxPos, TrackTable table)
        {
            // INDX ヘッダ:
            // [0-3]  'INDX'
            // [4-7]  予約 (00 00 00 00)
            // [8-9]  chunkLength (BE16)   : "INDX" からの長さ
            // [10-11]indexCount  (BE16)   : インデックス数 N
            // ここまでで 12 バイト (0x0C)
            const int indxHeaderSize = 0x0C;

            if (indxPos < 0 || indxPos + indxHeaderSize > data.Length)
                return;

            int chunkLength = ReadBe16(data, indxPos + 0x08);
            int indexCount = ReadBe16(data, indxPos + 0x0A);

            if (chunkLength < indxHeaderSize || indexCount <= 0)
                return;

            int indxEnd = indxPos + chunkLength;
            if (indxEnd > data.Length) indxEnd = data.Length;

            int ctrlStart = indxPos + indxHeaderSize;
            int timeStart = ctrlStart + 4 * indexCount;

            if (timeStart > indxEnd)
                return;

            // インデックス制御子を先に全部読む
            var indexCtrls = new uint[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                indexCtrls[i] = ReadBe32u(data, ctrlStart + 4 * i);
            }

            // SUB0 の位置 → 親インデックス番号 の対応
            // （同じ SUB0 を複数インデックスが指す可能性も考えつつ、最初のものを優先）
            var subPositions = new Dictionary<int, int>();

            // INDX 本体を全部 TrackIndex に起こしつつ、
            // サブインデックスを持たないものはそのまま Segment にする。
            for (int i = 0; i < indexCount; i++)
            {
                uint ctrl = indexCtrls[i];
                int controlOffset = ctrlStart + 4 * i;
                int pairPos = timeStart + 8 * i;
                if (pairPos + 8 > indxEnd)
                    break;

                var index = new TrackIndex
                {
                    IndexNumber = i,                 // 0 始まり
                    ControlWord = ctrl,
                    RawOffset = indxPos,
                    RawLength = indxEnd - indxPos,
                    ControlOffset = controlOffset,
                    TimePairOffset = pairPos,
                };
                table.Indices.Add(index);

                if (ctrl == 0x80000000u)
                {
                    // サブインデックスを持つインデックス → マーカーから SUB0 オフセットを読む
                    byte b0 = data[pairPos + 0];
                    byte b1 = data[pairPos + 1];
                    byte b2 = data[pairPos + 2];
                    byte b3 = data[pairPos + 3];

                    // [0] 0x00, [1-2] BE16 offset, [3] 0x00 の想定
                    int subOffset = (b1 << 8) | b2;
                    int subPos = indxPos + subOffset;
                    if (b0 == 0x00 && b3 == 0x00 && subPos >= 0 && subPos < data.Length)
                    {
                        if (!subPositions.ContainsKey(subPos))
                        {
                            subPositions[subPos] = i;
                        }
                    }
                    // このインデックス自体には直接再生区間が無いので、Segment は作らない
                }
                else
                {
                    // 通常インデックス → start/end 時刻を読む
                    if (TryParseTimeCode(data, pairPos, out int startFrame) &&
                        TryParseTimeCode(data, pairPos + 4, out int endFrame))
                    {
                        if (endFrame < startFrame)
                            (startFrame, endFrame) = (endFrame, startFrame);

                        index.StartFrame = startFrame;
                        index.EndFrame = endFrame;

                        table.Segments.Add(new Segment
                        {
                            Index = table.Segments.Count,
                            StartFrame = startFrame,
                            EndFrame = endFrame,
                            SourceIndex = index,
                            SourceSubIndex = null
                        });
                    }
                }
            }

            // SUB0 をパースしてサブインデックスを Segment に追加
            foreach (var kv in subPositions)
            {
                int subPos = kv.Key;
                int parentIndexNumber = kv.Value;

                if (parentIndexNumber < 0 || parentIndexNumber >= table.Indices.Count)
                    continue;

                var parentIndex = table.Indices[parentIndexNumber];
                ParseSub0Chunk(data, subPos, table, parentIndex);
            }
        }

        /// <summary>
        /// SUB0 チャンクをパースし、サブインデックスの start/end をTrackSubIndex と Segment の両方として追加する。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="subPos"></param>
        /// <param name="table"></param>
        /// <param name="parentIndex"></param>
        private static void ParseSub0Chunk(byte[] data, int subPos, TrackTable table, TrackIndex parentIndex)
        {
            // SUB0:
            // [0-3]  'SUB0'
            // [4-7]  予約 (00 00 00 00)
            // [8-9]  subLength  (BE16) : "SUB0" からの長さ
            // [10-11]subCount   (BE16) : サブインデックス数 M
            // [12-]  subCtrl[M]        : 4 バイト × M
            // [12+4M-] times           : [start(4)][end(4)] × M
            if (!MatchTag(data, subPos, "SUB0"))
                return;

            const int subHeaderSize = 0x0C;

            if (subPos < 0 || subPos + subHeaderSize > data.Length)
                return;

            int subLength = ReadBe16(data, subPos + 0x08);
            int subCount = ReadBe16(data, subPos + 0x0A);
            if (subLength < subHeaderSize || subCount <= 0)
                return;

            int subEnd = subPos + subLength;
            if (subEnd > data.Length) subEnd = data.Length;

            int ctrlStart = subPos + subHeaderSize;
            int timeStart = ctrlStart + 4 * subCount;
            if (timeStart > subEnd)
                return;

            for (int i = 0; i < subCount; i++)
            {
                int pairPos = timeStart + 8 * i;
                if (pairPos + 8 > subEnd)
                    break;

                uint subCtrl = ReadBe32u(data, ctrlStart + 4 * i);

                if (TryParseTimeCode(data, pairPos, out int startFrame) &&
                    TryParseTimeCode(data, pairPos + 4, out int endFrame))
                {
                    if (endFrame < startFrame)
                        (startFrame, endFrame) = (endFrame, startFrame);

                    var sub = new TrackSubIndex
                    {
                        SubNumber = i,  // 0 始まり
                        Parent = parentIndex,
                        ControlWord = subCtrl,
                        RawOffset = subPos,
                        RawLength = subEnd - subPos,
                        StartFrame = startFrame,
                        EndFrame = endFrame
                    };
                    parentIndex.SubIndices.Add(sub);

                    table.Segments.Add(new Segment
                    {
                        Index = table.Segments.Count,
                        StartFrame = startFrame,
                        EndFrame = endFrame,
                        SourceIndex = parentIndex,
                        SourceSubIndex = sub
                    });
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="tag"></param>
        /// <returns></returns>
        private static bool MatchTag(byte[] data, int offset, string tag)
        {
            if (offset + 4 > data.Length) return false;
            return data[offset] == (byte)tag[0] &&
                   data[offset + 1] == (byte)tag[1] &&
                   data[offset + 2] == (byte)tag[2] &&
                   data[offset + 3] == (byte)tag[3];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="tag"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        private static int FindChunk(byte[] data, string tag, int start)
        {
            for (int i = start; i <= data.Length - 4; i++)
            {
                if (MatchTag(data, i, tag))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static int ReadBe16(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static int ReadBe32(byte[] data, int offset)
        {
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static uint ReadBe32u(byte[] data, int offset)
        {
            return (uint)ReadBe32(data, offset);
        }

        /// <summary>
        /// [mm_bcd][ss_bcd][ff_bcd][00] を 75fps のフレーム数に変換。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="frame"></param>
        /// <returns></returns>
        private static bool TryParseTimeCode(byte[] data, int offset, out int frame)
        {
            frame = 0;
            if (offset + 4 > data.Length) return false;

            byte mmBcd = data[offset + 0];
            byte ssBcd = data[offset + 1];
            byte ffBcd = data[offset + 2];
            byte flag = data[offset + 3];

            // BCD解釈
            if (!IsBcd(mmBcd) || !IsBcd(ssBcd) || !IsBcd(ffBcd))
                return false;

            int mm = BcdToInt(mmBcd);
            int ss = BcdToInt(ssBcd);
            int ff = BcdToInt(ffBcd);

            if (mm < 0 || mm > 99) return false;
            if (ss < 0 || ss > 59) return false;
            if (ff < 0 || ff > 74) return false;

            // 今回の仕様では flag は常に 0x00 とみなしてよい
            // ⇒なんか知らんが01もあるらしい
            // if (flag != 0x00) return false;

            frame = (mm * 60 + ss) * 75 + ff;
            return true;
        }

        private static bool IsBcd(byte b)
        {
            int hi = (b >> 4) & 0xF;
            int lo = b & 0xF;
            return hi <= 9 && lo <= 9;
        }

        private static int BcdToInt(byte b)
        {
            int hi = (b >> 4) & 0xF;
            int lo = b & 0xF;
            return hi * 10 + lo;
        }

        /// <summary>
        /// フレーム（75fps）を "mm:ss_ff" 形式へ。
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public static string FormatFrameAsTimeWithSector(int frame)
        {
            int ff = frame % 75;
            int totalSeconds = frame / 75;
            int ss = totalSeconds % 60;
            int mm = totalSeconds / 60;
            return $"{mm:00}:{ss:00}_{ff:00}";
        }
    }
}
