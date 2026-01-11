namespace SerializerFoundation;

public interface IWriteBuffer : IDisposable
{
    /// <summary>
    /// Returns a Span to write to that is at least the requested length (specified by <paramref name="sizeHint"/>).
    /// If no <paramref name="sizeHint"/> is provided (or it's equal to 0), some non-empty buffer is returned.
    /// </summary>
    Span<byte> GetSpan(int sizeHint = 0);

    /// <summary>
    /// Returns a Span reference to write to that is at least the requested length (specified by <paramref name="sizeHint"/>).
    /// If no <paramref name="sizeHint"/> is provided (or it's equal to 0), some non-empty buffer is returned.
    /// </summary>
    ref byte GetReference(int sizeHint = 0);

    void Advance(int bytesWritten);
    long BytesWritten { get; }

    // TODO: need Flush.
}

public static class WriteBufferExtensions
{
    extension<TWriteBuffer>(ref TWriteBuffer buffer)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    {
        public int IntWrittenCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return checked((int)buffer.BytesWritten);
            }
        }
    }
}
