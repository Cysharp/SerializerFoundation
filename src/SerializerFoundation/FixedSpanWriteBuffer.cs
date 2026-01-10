using System.IO.Pipelines;
using System.Text.Json;

namespace SerializerFoundation;

public ref struct FixedSpanWriteBuffer : IWriteBuffer
{
    Span<byte> buffer;
    int written;

    public long BytesWritten => written;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedSpanWriteBuffer(Span<byte> buffer)
    {
        this.buffer = buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }
        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        buffer = buffer.Slice(bytesWritten);
        written += bytesWritten;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        // No-op for SpanBuffer
    }
}
