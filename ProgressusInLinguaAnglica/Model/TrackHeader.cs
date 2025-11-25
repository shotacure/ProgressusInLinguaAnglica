namespace ProgressusInLinguaAnglica.Model
{
    /// <summary>
    /// トラックヘッダー構造定義
    /// </summary>
    public sealed class TrackHeader
    {
        /// <summary>
        /// TRCK+0x08-09 トラック全体(ヘッダー+テーブル)のサイズ
        /// </summary>
        public int Size { get; init; }

        /// <summary>
        /// TRCK+0x0A-0B 参照先ファイル番号 (PROGRESS IN ENGLISHでは 1 固定)
        /// </summary>
        public int FileNo { get; init; }

        /// <summary>
        /// TRCK+0x0C 参照先チャンネル番号 (0～15)
        /// </summary>
        public int Channel { get; init; }

        /// <summary>
        /// TRCK+0x0D トラック内の親インデックス数
        /// </summary>
        public int IndexLength { get; init; }

        /// <summary>
        /// TRCK+0x0E-0F ヘッダのサイズ (PROGRESS IN ENGLISHでは 16 固定)
        /// </summary>
        public int HeaderSize { get; init; }
    }
}
