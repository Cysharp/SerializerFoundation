using System.Diagnostics.CodeAnalysis;

namespace SerializationFramework;

internal static class Throws
{
    [DoesNotReturn]
    internal static void InsufficientSpaceInBuffer() => throw new InvalidOperationException("Insufficient space in buffer.");
}



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


