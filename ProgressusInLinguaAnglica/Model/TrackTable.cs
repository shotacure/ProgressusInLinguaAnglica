using System.Collections.Generic;

namespace ProgressusInLinguaAnglica.Model
{
    /// <summary>
    /// 連続再生フラグ
    /// </summary>
    public enum PlaybackContinuation
    {
        Stop,           // 先頭バイト x0
        NextIndex,      // 先頭バイト x1
        NextSubIndex    // 先頭バイト x2
    }

    /// <summary>
    /// セグメント種別
    /// </summary>
    public enum SegmentKind
    {
        Regular,        // 2バイト目 0x01 その他
        Question,       // 2バイト目 0x09
        CorrectAnswer,  // 2バイト目 0x05
        WrongAnswer     // 2バイト目 0x03
    }

    /// <summary>
    /// トラック構造定義 - TBLファイル、トラック、インデックス、サブインデックスの階層構造および再生用のセグメント情報を定義する。
    /// </summary>
    public sealed class TrackTable
    {
        /// <summary>
        /// TBLファイル名 (Cnnn.TBL)
        /// </summary>
        public string FileName { get; init; } = "";

        /// <summary>
        /// トラックヘッダー
        /// </summary>
        public TrackHeader Header { get; init; } = null!;

        /// <summary>
        /// TBLファイルの生バイト列
        /// </summary>
        public byte[] RawData { get; init; } = System.Array.Empty<byte>();

        /// <summary>
        /// インデックスリスト - インデックス番号は 0 から始まる。ちな indices はラテン語における第3変化名詞 index の複数主格形。
        /// </summary>
        public List<TrackIndex> Indices { get; } = new();

        /// <summary>
        /// 初期再生用セグメント - 特に指定がなければ最初の Segment が入る。
        /// </summary>
        public Segment? MainSegment { get; set; }

        /// <summary>
        /// セグメントリスト - INDX / SUB0 から得られた全ての [start, end] の区間をフラットにしたビュー。
        /// </summary>
        public List<Segment> Segments { get; } = new();
    }

    /// <summary>
    /// インデックス構造定義
    /// </summary>
    public sealed class TrackIndex
    {
        /// <summary>
        /// インデックス番号 (0 始まり)
        /// </summary>
        public int IndexNumber { get; init; }

        /// <summary>
        /// インデックス制御子 (4 バイト x インデックス数)
        /// </summary>
        public uint ControlWord { get; init; }

        /// <summary>
        /// この INDX チャンク先頭のファイルオフセット
        /// </summary>
        public int RawOffset { get; init; }

        /// <summary>
        /// チャンク長 (バイト数)
        /// </summary>
        public int RawLength { get; init; }

        /// <summary>
        /// 制御子アドレスオフセット
        /// </summary>
        public int ControlOffset { get; init; }

        /// <summary>
        /// 始点終了フレームアドレスオフセット
        /// </summary>
        public int TimePairOffset { get; init; }

        /// <summary>
        /// インデックス開始フレーム (mm:ss_ff → 75fps)
        /// サブインデックス (0x8000_0000) の場合は null
        /// </summary>
        public int? StartFrame { get; set; }

        /// <summary>
        /// インデックス終了フレーム。
        /// サブインデックス (0x8000_0000) の場合は null
        /// </summary>
        public int? EndFrame { get; set; }

        /// <summary>
        /// 連続再生フラグ
        /// </summary>
        public PlaybackContinuation PlaybackContinuation { get; set; }
        
        /// <summary>
        /// セグメント種別フラグ
        /// </summary>
        public SegmentKind SegmentKind { get; set; }

        /// <summary>
        /// サブインデックスリスト
        /// </summary>
        public List<TrackSubIndex> SubIndices { get; } = new();
    }

    /// <summary>
    /// SUB0 に相当する 1 サブインデックス。
    /// </summary>
    public sealed class TrackSubIndex
    {
        /// <summary>
        /// サブインデックス番号（0 始まり）
        /// </summary>
        public int SubNumber { get; init; }

        /// <summary>
        /// 親インデックス
        /// </summary>
        public TrackIndex Parent { get; init; } = null!;

        /// <summary>
        /// SUB0 内の制御ワード (4 バイト)
        /// </summary>
        public uint ControlWord { get; init; }

        /// <summary>
        /// この SUB0 チャンク先頭のファイルオフセット
        /// </summary>
        public int RawOffset { get; init; }

        /// <summary>
        /// この SUB0 チャンクの長さ（バイト数）
        /// </summary>
        public int RawLength { get; init; }

        /// <summary>
        /// サブインデックスの開始フレーム
        /// </summary>
        public int StartFrame { get; init; }

        /// <summary>
        /// サブインデックスの開始フレームの最終バイト (用途不明)
        /// </summary>
        public byte StartByte { get; init; }

        /// <summary>
        /// サブインデックスの終了フレーム
        /// </summary>
        public int EndFrame { get; init; }

        /// <summary>
        /// サブインデックスの終了フレームの最終バイト (用途不明)
        /// </summary>
        public byte EndByte { get; init; }

        /// <summary>
        /// 連続再生フラグ
        /// </summary>
        public PlaybackContinuation PlaybackContinuation { get; set; }

        /// <summary>
        /// セグメント種別フラグ
        /// </summary>
        public SegmentKind SegmentKind { get; set; }
    }

    /// <summary>
    /// 既存 UI／再生用のフラットな区間情報。
    /// </summary>
    public sealed class Segment
    {
        /// <summary>
        /// 便宜上の番号（0,1,2,...）
        /// インデックス番号とは独立。
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 開始フレーム (mm:ss_ff → 75fps)
        /// </summary>
        public int StartFrame { get; set; }

        /// <summary>
        /// 開始フレームの最終バイト (用途不明)
        /// </summary>
        public byte StartByte { get; init; }

        /// <summary>
        /// 終了フレーム
        /// </summary>
        public int EndFrame { get; set; }

        /// <summary>
        /// 終了フレームの最終バイト (用途不明)
        /// </summary>
        public byte EndByte { get; init; }

        /// <summary>
        /// この Segment の元になった INDX（あれば）
        /// SUB0 由来の場合も親インデックスが入る。
        /// </summary>
        public TrackIndex? SourceIndex { get; init; }

        /// <summary>
        /// この Segment の元になった SUB0（あれば）
        /// </summary>
        public TrackSubIndex? SourceSubIndex { get; init; }
    }
}
