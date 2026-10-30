using System.Buffers;

namespace SerializerFoundation.Tests;

// CopyTo and WriteTo only copy into their destination, so the parameter is `scoped` and stack memory can be
// passed even when the buffer outlives the call: a buffer built over heap memory, or one reached by ref.
// Without `scoped` the compiler must assume the buffer could keep the destination, and every stackalloc'd
// argument below fails with CS8350, so this file is itself the regression check.
public class ScopedDestinationTest
{
    static readonly byte[] Data = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19];

    sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    // split so that a 4-byte copy from offset 2 straddles the seam and takes the segment-walking slow path
    static ReadOnlySequence<byte> TwoSegments()
    {
        var first = new Segment(Data.AsMemory(0, 4));
        var last = first.Append(Data.AsMemory(4));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    // the generic path: the interface member's own signature has to promise `scoped`
    static void CopyToStackThroughInterface<TReadBuffer>(ref TReadBuffer buffer)
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        buffer.Advance(2);
        Span<byte> destination = stackalloc byte[4];
        buffer.CopyTo(destination);
        Assert.Equal(Data.AsSpan(2, 4).ToArray(), destination.ToArray());
    }

    [Fact]
    public void CopyTo_AcceptsStackMemory_OnEveryReadBuffer()
    {
        Span<byte> destination = stackalloc byte[4];

        var span = new ReadOnlySpanReadBuffer(Data);
        span.CopyTo(destination);
        Assert.Equal(Data.AsSpan(0, 4).ToArray(), destination.ToArray());
        CopyToStackThroughInterface(ref span);

        var sequence = new ReadOnlySequenceReadBuffer(TwoSegments());
        try
        {
            sequence.CopyTo(destination);
            Assert.Equal(Data.AsSpan(0, 4).ToArray(), destination.ToArray());
            CopyToStackThroughInterface(ref sequence);
        }
        finally { sequence.Dispose(); }

        var spanBacked = new ReadOnlySequenceReadBuffer(Data.AsSpan());
        try { CopyToStackThroughInterface(ref spanBacked); }
        finally { spanBacked.Dispose(); }

        var compatibleSequence = new CompatibleReadOnlySequenceReadBuffer(TwoSegments());
        try { CopyToStackThroughInterface(ref compatibleSequence); }
        finally { compatibleSequence.Dispose(); }

        unsafe
        {
            fixed (byte* p = Data)
            {
                var compatibleSpan = new CompatibleReadOnlySpanReadBuffer(p, Data.Length);
                CopyToStackThroughInterface(ref compatibleSpan);
            }
        }
    }

    static void Write<TWriteBuffer>(ref TWriteBuffer buffer)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    {
        // 5-byte chunks past an 8-byte scratch, so the message spans scratch and pooled segments
        for (var offset = 0; offset < Data.Length; offset += 5)
        {
            Data.AsSpan(offset, 5).CopyTo(buffer.GetSpan(5));
            buffer.Advance(5);
        }
    }

    static void WriteToStackByRef(ref ArrayPoolListWriteBuffer buffer)
    {
        Span<byte> destination = stackalloc byte[Data.Length];
        buffer.WriteTo(destination);
        Assert.Equal(Data, destination.ToArray());
    }

    [Fact]
    public void WriteTo_AcceptsStackMemory()
    {
        Span<byte> destination = stackalloc byte[Data.Length];

        var buffer = new ArrayPoolListWriteBuffer(new byte[8]);
        try
        {
            Write(ref buffer);
            buffer.WriteTo(destination);
            Assert.Equal(Data, destination.ToArray());
            WriteToStackByRef(ref buffer);
        }
        finally { buffer.Dispose(); }

        var compatible = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            Write(ref compatible);
            destination.Clear();
            compatible.WriteTo(destination);
            Assert.Equal(Data, destination.ToArray());
        }
        finally { compatible.Dispose(); }
    }
}
