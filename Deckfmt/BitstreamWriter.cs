namespace AlteredBgaApi.Deckfmt;

/// <summary>
/// Writes numbers of varying bitlengths to a byte buffer.
/// All data is written in big-endian (network) byte order.
/// </summary>
public class BitstreamWriter
{
    private readonly List<byte> _buffer = new();
    private ulong _pendingByte;
    private int _pendingBits;
    private int _offset;

    /// <summary>How many bits have been written via this writer in total.</summary>
    public int Offset => _offset;

    /// <summary>How many bits into the current byte is the write cursor.</summary>
    public int ByteOffset => _pendingBits;

    /// <summary>
    /// Write the given number to the bitstream with the given bitlength.
    /// If the number is too large for the number of bits specified,
    /// the lower-order bits are written and the higher-order bits are ignored.
    /// </summary>
    public void Write(int length, uint value)
    {
        var valueN = value % (1UL << length);
        var remainingLength = length;

        while (remainingLength > 0)
        {
            var shift = 8 - _pendingBits - remainingLength;
            ulong contribution;
            int writtenLength;

            if (shift >= 0)
            {
                contribution = valueN << shift;
                writtenLength = remainingLength;
            }
            else
            {
                contribution = valueN >> -shift;
                writtenLength = Math.Min(-shift, 8 - _pendingBits);
            }

            _pendingByte |= contribution;
            _pendingBits += writtenLength;
            _offset += writtenLength;

            remainingLength -= writtenLength;
            valueN %= 1UL << remainingLength;

            if (_pendingBits == 8)
            {
                FinishByte();
            }
        }
    }

    /// <summary>Finish the current byte (assuming zeros for remaining bits) and flush.</summary>
    public byte[] ToArray()
    {
        if (_pendingBits > 0)
        {
            _buffer.Add((byte)_pendingByte);
            _pendingBits = 0;
            _pendingByte = 0;
        }

        return _buffer.ToArray();
    }

    private void FinishByte()
    {
        if (_pendingBits > 0)
        {
            _buffer.Add((byte)_pendingByte);
            _pendingBits = 0;
            _pendingByte = 0;
        }
    }
}
