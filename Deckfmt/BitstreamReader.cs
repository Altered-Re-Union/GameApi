namespace AlteredBgaApi.Deckfmt;

/// <summary>
/// Reads numbers of varying bitlengths from a byte buffer.
/// All data is read in big-endian (network) byte order.
/// </summary>
public class BitstreamReader
{
    private readonly byte[] _buffer;
    private int _offset;
    private int _bufferedLength;

    /// <summary>Current offset in bits.</summary>
    public int Offset => _offset;

    /// <summary>Number of bits available for reading.</summary>
    public int Available => _bufferedLength - _offset;

    public BitstreamReader(byte[] buffer)
    {
        _buffer = buffer;
        _bufferedLength = buffer.Length * 8;
    }

    /// <summary>
    /// Read an unsigned integer of the given bit length.
    /// </summary>
    public uint ReadSync(int length)
    {
        if (Available < length)
            throw new InvalidOperationException($"Not enough bits available (requested={length}, available={Available})");

        uint value = 0;
        var remainingLength = length;
        var offset = _offset;

        while (remainingLength > 0)
        {
            var byteOffset = offset / 8;
            var bitOffset = offset % 8;
            var currentByte = _buffer[byteOffset];

            var bitContribution = Math.Min(8 - bitOffset, remainingLength);
            var mask = (1U << bitContribution) - 1;
            var extracted = (uint)((currentByte >> (8 - bitContribution - bitOffset)) & mask);

            value = (value << bitContribution) | extracted;

            offset += bitContribution;
            remainingLength -= bitContribution;
        }

        _offset = offset;
        return value;
    }
}
