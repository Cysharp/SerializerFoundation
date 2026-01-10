using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SerializerFoundation;

public interface IWriteBuffer
{
    /// <summary>
    /// Returns a Span to write to that is at least the requested length (specified by <paramref name="sizeHint"/>).
    /// If no <paramref name="sizeHint"/> is provided (or it's equal to 0), some non-empty buffer is returned.
    /// </summary>
    Span<byte> GetSpan(int sizeHint = 0);

    void Advance(int bytesWritten);
    void Flush();
    long BytesWritten { get; }
}

public interface IReadBuffer
{
    /// <summary>
    /// Returns a Span to read to that is at least the requested length (specified by <paramref name="sizeHint"/>).
    /// If no <paramref name="sizeHint"/> is provided (or it's equal to 0), some non-empty buffer is returned.
    /// </summary>
    ReadOnlySpan<byte> GetSpan(int sizeHint = 0);
    void Advance(int bytesConsumed);
    long BytesConsumed { get; }
}

// TODO: WrittenCount?
public interface IAsyncWriteBuffer
{
    bool TryGetSpan(int byteCount, out Span<byte> span);
    ValueTask EnsureBufferAsync(int byteCount, CancellationToken cancellationToken);
    void Advance(int bytesConsumed);
    ValueTask FlushAsync(CancellationToken cancellationToken);
}

public interface IAsyncReadBuffer
{
    bool TryGetSpan(int byteCount, out ReadOnlySpan<byte> span);
    ValueTask EnsureBufferAsync(int byteCount, CancellationToken cancellationToken);
    void Advance(int bytesConsumed);
}

public static class WriteBufferExtensions
{
    extension<TWriteBuffer>(ref TWriteBuffer buffer)
        where TWriteBuffer : struct, IWriteBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref byte GetSpanReference(int byteCount)
        {
            return ref MemoryMarshal.GetReference(buffer.GetSpan(byteCount));
        }

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

public static class ReadBufferExtensions
{
    extension<TReadBuffer>(ref TReadBuffer buffer)
        where TReadBuffer : struct, IReadBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref byte GetSpanReference(int byteCount)
        {
            return ref MemoryMarshal.GetReference(buffer.GetSpan(byteCount));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Peek()
        {
            return MemoryMarshal.GetReference(buffer.GetSpan(1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> Peek(int byteCount)
        {
            return buffer.GetSpan(byteCount);
        }
    }
}
