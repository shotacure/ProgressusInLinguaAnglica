using System.Collections.Generic;

namespace ProgressusInLinguaAnglica.Model
{
    /// <summary>
    /// 1つの TBL (チャプター) に対応する情報。
    /// TBL の生データ / TRCK ヘッダ / INDX / SUB0 の階層構造と、
    /// 既存 UI 用のフラットな Segment ビューの両方を持つ。
    /// </summary>
    public sealed class TrackTable
    {
        /// <summary>元の TBL ファイル名（拡張子込み）。</summary>
        public string FileName { get; init; } = "";

        /// <summary>TRCK ヘッダ情報。</summary>
        public TrackHeader Header { get; init; } = null!;

        /// <summary>
        /// TBL ファイルの生バイト列。
        /// 「TBL に含まれる全情報を保持する」ための保険。
        /// </summary>
        public byte[] RawData { get; init; } = System.Array.Empty<byte>();

        /// <summary>
        /// INDX / SUB0 の階層構造のルート。
        /// IndexNumber は 0 から始まる。
        /// </summary>
        public List<TrackIndex> Indices { get; } = new();

        /// <summary>
        /// 再生などで使うメインのセグメント。
        /// 特に指定がなければ最初の Segment が入る。
        /// </summary>
        public Segment? MainSegment { get; set; }

        /// <summary>
        /// INDX / SUB0 から得られた全ての [start, end] の区間を
        /// フラットにしたビュー。
        /// 既存 UI はこれを使えばよい。
        /// </summary>
        public List<Segment> Segments { get; } = new();
    }

    /// <summary>
    /// INDX に相当する 1 インデックス。
    /// </summary>
    public sealed class TrackIndex
    {
        /// <summary>インデックス番号（0 始まり）。</summary>
        public int IndexNumber { get; init; }

        /// <summary>INDX の制御ワード (4 バイト)。</summary>
        public uint ControlWord { get; init; }

        /// <summary>この INDX チャンク先頭のファイルオフセット。</summary>
        public int RawOffset { get; init; }

        /// <summary>この INDX チャンクの長さ（バイト数）。</summary>
        public int RawLength { get; init; }

        /// <summary>制御ワードが格納されているファイル上のオフセット。</summary>
        public int ControlOffset { get; init; }

        /// <summary>対応する time pair (start) が格納されているファイル上のオフセット。</summary>
        public int TimePairOffset { get; init; }

        /// <summary>
        /// インデックス本体の開始フレーム (mm:ss_ff → 75fps)。
        /// サブインデックス専用 (0x8000_0000) の場合は null。
        /// </summary>
        public int? StartFrame { get; set; }

        /// <summary>インデックス本体の終了フレーム。</summary>
        public int? EndFrame { get; set; }

        /// <summary>このインデックスに属するサブインデックス群。</summary>
        public List<TrackSubIndex> SubIndices { get; } = new();
    }

    /// <summary>
    /// SUB0 に相当する 1 サブインデックス。
    /// </summary>
    public sealed class TrackSubIndex
    {
        /// <summary>サブインデックス番号（0 始まり）。</summary>
        public int SubNumber { get; init; }

        /// <summary>親インデックス。</summary>
        public TrackIndex Parent { get; init; } = null!;

        /// <summary>SUB0 内の制御ワード (4 バイト)。</summary>
        public uint ControlWord { get; init; }

        /// <summary>この SUB0 チャンク先頭のファイルオフセット。</summary>
        public int RawOffset { get; init; }

        /// <summary>この SUB0 チャンクの長さ（バイト数）。</summary>
        public int RawLength { get; init; }

        /// <summary>サブインデックスの開始フレーム。</summary>
        public int StartFrame { get; init; }

        /// <summary>サブインデックスの終了フレーム。</summary>
        public int EndFrame { get; init; }
    }

    /// <summary>
    /// 既存 UI／再生用のフラットな区間情報。
    /// </summary>
    public sealed class Segment
    {
        /// <summary>
        /// 便宜上の番号（0,1,2,...）。
        /// インデックス番号とは独立。
        /// </summary>
        public int Index { get; set; }

        /// <summary>開始フレーム (mm:ss_ff → 75fps)。</summary>
        public int StartFrame { get; set; }

        /// <summary>終了フレーム。</summary>
        public int EndFrame { get; set; }

        /// <summary>
        /// この Segment の元になった INDX（あれば）。
        /// SUB0 由来の場合も親インデックスが入る。
        /// </summary>
        public TrackIndex? SourceIndex { get; init; }

        /// <summary>この Segment の元になった SUB0（あれば）。</summary>
        public TrackSubIndex? SourceSubIndex { get; init; }
    }
}
