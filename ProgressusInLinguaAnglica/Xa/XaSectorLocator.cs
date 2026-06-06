using System;
using System.Collections.Generic;

namespace ProgressusInLinguaAnglica.Xa
{
    /// <summary>
    /// SOUND.RTF を全走査せず、指定チャネル・フレーム区間のセクタ群を
    /// オンデマンドで取り出す。XA セクタはファイル内で時刻(MSF)が単調増加で
    /// 並んでいるため、二分探索で開始位置を特定し、その区間だけを
    /// シーケンシャルに読み出してユーザーデータを連結する。
    /// </summary>
    public sealed class XaSectorLocator
    {
        /// <summary>バルク読み込みのチャンク（セクタ数）。</summary>
        private const int ChunkSectors = 64;

        private readonly XaRiffReader _reader;

        // 直近に抽出したセグメントの簡易 LRU キャッシュ（連続再生・保存の再抽出を避ける）。
        private const int CacheCapacity = 8;
        private readonly object _cacheLock = new();
        private readonly LinkedList<(long key, byte[] data)> _cache = new();

        /// <summary>
        /// ロケーターを生成する。インデックスの事前構築は行わない（O(1)）。
        /// </summary>
        /// <param name="reader">XA RIFF リーダー</param>
        public XaSectorLocator(XaRiffReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        /// <summary>
        /// 指定チャネルの [startFrame, endFrame] 区間に含まれるセクタのユーザーデータ
        /// （2336 バイト×セクタ数）を連結して返す。区間外まで全走査はしない。
        /// </summary>
        /// <param name="channel">チャネル番号</param>
        /// <param name="startFrame">開始フレーム（含む）</param>
        /// <param name="endFrame">終了フレーム（含む）</param>
        /// <returns>連結済み XA ユーザーデータ。該当なしなら空配列。</returns>
        public byte[] ReadSegmentUserData(int channel, int startFrame, int endFrame)
        {
            if (endFrame < startFrame || _reader.SectorCount == 0)
                return Array.Empty<byte>();

            long cacheKey = MakeKey(channel, startFrame, endFrame);
            if (TryGetCache(cacheKey, out var cached))
                return cached;

            long startOrd = FindFirstSectorAtOrAfter(startFrame);
            if (startOrd >= _reader.SectorCount)
                return Array.Empty<byte>();

            using var ms = new System.IO.MemoryStream();
            var buf = new byte[ChunkSectors * XaRiffReader.SectorSize];

            long ord = startOrd;
            bool done = false;
            while (ord < _reader.SectorCount && !done)
            {
                int got = _reader.ReadSectors(ord, buf, ChunkSectors);
                if (got <= 0) break;

                for (int i = 0; i < got; i++)
                {
                    int b = i * XaRiffReader.SectorSize;

                    int mm = BcdToInt(buf[b + 12]);
                    int ss = BcdToInt(buf[b + 13]);
                    int ff = BcdToInt(buf[b + 14]);
                    int totalFrame = (mm * 60 + ss) * 75 + ff;

                    if (totalFrame > endFrame)
                    {
                        done = true;
                        break;
                    }

                    int ch = buf[b + 17];
                    if (totalFrame >= startFrame && ch == channel)
                    {
                        // ユーザーデータはセクタ先頭から 16 バイト目以降の 2336 バイト。
                        ms.Write(buf, b + 16, XaRiffReader.UserDataSize);
                    }
                }

                ord += got;
            }

            byte[] result = ms.ToArray();
            AddCache(cacheKey, result);
            return result;
        }

        /// <summary>
        /// 絶対フレームが frame 以上になる最初のセクタ番号を二分探索で求める。
        /// 見つからなければ SectorCount を返す。
        /// </summary>
        /// <param name="frame">探索するフレーム</param>
        /// <returns>セクタ番号（0 始まり）</returns>
        private long FindFirstSectorAtOrAfter(int frame)
        {
            long lo = 0;
            long hi = _reader.SectorCount; // 上限は排他的

            while (lo < hi)
            {
                long mid = lo + (hi - lo) / 2;
                if (_reader.TryReadSectorHeader(mid, out int tf, out _) && tf < frame)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        //=====================================================================
        //  簡易 LRU キャッシュ
        //=====================================================================

        private static long MakeKey(int channel, int startFrame, int endFrame)
        {
            // channel(8bit) | start(24bit) | end(24bit) を 1 つの long に詰める。
            return ((long)(channel & 0xFF) << 48)
                 | ((long)(startFrame & 0xFFFFFF) << 24)
                 | (uint)(endFrame & 0xFFFFFF);
        }

        private bool TryGetCache(long key, out byte[] data)
        {
            lock (_cacheLock)
            {
                for (var node = _cache.First; node is not null; node = node.Next)
                {
                    if (node.Value.key == key)
                    {
                        data = node.Value.data;
                        _cache.Remove(node);
                        _cache.AddFirst(node); // 参照されたものを先頭へ
                        return true;
                    }
                }
            }
            data = Array.Empty<byte>();
            return false;
        }

        private void AddCache(long key, byte[] data)
        {
            if (data.Length == 0) return;
            lock (_cacheLock)
            {
                _cache.AddFirst((key, data));
                while (_cache.Count > CacheCapacity)
                    _cache.RemoveLast();
            }
        }

        private static int BcdToInt(byte b)
        {
            int hi = (b >> 4) & 0xF;
            int lo = b & 0xF;
            return hi * 10 + lo;
        }
    }
}
