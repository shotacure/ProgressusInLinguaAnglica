namespace ProgressusInLinguaAnglica.Model
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class TrackHeader
    {
        public int Size { get; init; }          // TRCK+0x08-09
        public int FileNo { get; init; }        // TRCK+0x0A-0B
        public int Channel { get; init; }       // TRCK+0x0C
        public int IndexLength { get; init; }      // TRCK+0x0D
        public int HeaderSize { get; init; }      // TRCK+0x0E-0F
    }
}
