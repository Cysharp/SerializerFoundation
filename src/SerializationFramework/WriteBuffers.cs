using System.Runtime.CompilerServices;

namespace SerializationFramework;

public ref struct FixedSpanBuffer : IWriteBuffer
{
    Span<byte> buffer;
    int written;

    public long WrittenCount => written;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedSpanBuffer(Span<byte> buffer)
    {
        this.buffer = buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int byteCount)
    {
        if (buffer.Length < byteCount)
        {
            Throws.InsufficientSpaceInBuffer();
        }

        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesConsumed)
    {
        buffer = buffer.Slice(bytesConsumed);
        written += bytesConsumed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        // No-op for SpanBuffer
    }
}

//public ref struct BufferWriterWriteBuffer(IBufferWriter<byte> bufferWriter) : IWriteBuffer
//{
//    Span<byte> span;
//    int written;

//    public Span<byte> GetSpan(int byteCount)
//    {
//        if (span.Length < byteCount)
//        {
//            bufferWriter.Advance(written);
//            span = bufferWriter.GetSpan(byteCount);
//            // TODO: check span length.
//        }

//        return span;
//    }

//    public void Advance(int bytesConsumed)
//    {
//        span = span.Slice(bytesConsumed);
//        written += bytesConsumed;
//    }

//    public void Flush()
//    {
//        if (written == 0) return;

//        bufferWriter.Advance(written);
//        span = default;
//        written = 0;
//    }
//}



// name chane -> ExpandableSequentailBuffer???
//public ref struct ArrayPoolSequentialBuffer : ISequentialBuffer, IDisposable
//{
//    ref byte reference;
//    int length;
//    int written;

//    Span<byte> scratchBuffer;
//    int writtenScratch;

//    ArrayPoolNode? head;
//    ArrayPoolNode? tail;

//    public ArrayPoolSequentialBuffer(Span<byte> scratchBuffer) // 512?
//    {
//        reference = ref MemoryMarshal.GetReference(scratchBuffer);
//        length = scratchBuffer.Length;
//    }

//    public void Advance(int count)
//    {
//        written += count;
//        if (head == null)
//        {
//            writtenScratch += count;
//        }
//    }

//    public void Flush()
//    {
//        throw new NotImplementedException();
//    }

//    public ref byte GetSpan(int sizeHint)
//    {
//        if (written + sizeHint > length)
//        {
//            RentNewBuffer();
//        }

//        return ref Unsafe.Add(ref reference, written);
//    }

//    void RentNewBuffer()
//    {
//        // new DefaultInterpolatedStringHandler().ToStringAndClear
//    }

//    public byte[] ToArrayAndClear()
//    {
//        throw new NotImplementedException();
//    }

//    public void Dispose()
//    {
//        throw new NotImplementedException();
//    }
//}

//internal sealed class ArrayPoolNode
//{
//    public byte[] Data;
//    public ArrayPoolNode? Next;

//    public ArrayPoolNode(int size)
//    {
//        Data = ArrayPool<byte>.Shared.Rent(size);
//    }

//    public ArrayPoolNode CreateNext()
//    {
//        var nextSize = (int)Math.Min(unchecked((uint)Data.Length * 2), (uint)Array.MaxLength);
//        var nextNode = new ArrayPoolNode(nextSize);
//        Next = nextNode;
//        return nextNode;
//    }
//}


