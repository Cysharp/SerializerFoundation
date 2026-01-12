namespace SerializerFoundation;

public interface IReadBuffer : IDisposable
{
    /// <summary>
    /// Returns a Span to read to that is at least the requested length (specified by <paramref name="sizeHint"/>).
    /// If no <paramref name="sizeHint"/> is provided (or it's equal to 0), some non-empty buffer is returned.
    /// </summary>
    ReadOnlySpan<byte> GetSpan(int sizeHint = 0);

    /// <summary>
    /// Returns a Span reference to read to that is at least the requested length (specified by <paramref name="sizeHint"/>).
    /// If no <paramref name="sizeHint"/> is provided (or it's equal to 0), some non-empty buffer is returned.
    /// </summary>
    ref readonly byte GetReference(int sizeHint = 0);

    void Advance(int bytesConsumed);
    long BytesConsumed { get; }
}

public static class ReadBufferExtensions
{
    extension<TReadBuffer>(ref TReadBuffer buffer)
        where TReadBuffer : struct, IReadBuffer
#if NET9_0_OR_GREATER
        , allows ref struct
#endif
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Peek()
        {
            return buffer.GetReference(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> Peek(int byteCount)
        {
            return buffer.GetSpan(byteCount);
        }
    }
}
