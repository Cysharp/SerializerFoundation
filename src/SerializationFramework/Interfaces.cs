using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SerializationFramework;

public interface IWriteBuffer
{
    Span<byte> GetSpan(int byteCount);
    void Advance(int bytesConsumed);
    void Flush();
    long WrittenCount { get; }
}

public interface IReadBuffer
{
    ReadOnlySpan<byte> GetSpan(int byteCount);
    void Advance(int bytesConsumed);
}

// TODO: WrittenCount
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
                return checked((int)buffer.WrittenCount);
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