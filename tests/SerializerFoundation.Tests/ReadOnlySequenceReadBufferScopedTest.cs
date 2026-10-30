using System.Buffers;
using SerializerFoundation.CodeAnalysis;

namespace SerializerFoundation.Tests;

// A serializer reader that owns its buffer builds it from a sequence received by value (or held in a local)
// and keeps it in a field. The constructors copy the sequence, so their `in` parameter is `scoped`;
// without it these shapes fail to compile with CS8347, so this file is itself the regression check.
public class ReadOnlySequenceReadBufferScopedTest
{
    [NonCopyable]
    ref struct OwningReader
    {
        ReadOnlySequenceReadBuffer buffer;

        public OwningReader(ReadOnlySequence<byte> sequence)
        {
            buffer = new ReadOnlySequenceReadBuffer(in sequence);
        }

        public OwningReader(ReadOnlySequence<byte> sequence, Span<byte> scratch)
        {
            buffer = new ReadOnlySequenceReadBuffer(in sequence, scratch);
        }

        public static OwningReader FromLocal(byte[] first, byte[] second)
        {
            var sequence = Chain(first, second);
            var reader = new OwningReader();
            reader.buffer = new ReadOnlySequenceReadBuffer(in sequence);
            return reader;
        }

        public byte[] Read(int count)
        {
            Assert.True(buffer.TryGetSpan(count, out var span));
            var result = span.Slice(0, count).ToArray();
            buffer.Advance(count);
            return result;
        }

        public void Dispose() => buffer.Dispose();
    }

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

    static ReadOnlySequence<byte> Chain(byte[] first, byte[] second)
    {
        var head = new Segment(first);
        var tail = head.Append(second);
        return new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
    }

    [Fact]
    public void FromByValueParameter_ReadsAcrossSeam()
    {
        using var reader = new OwningReader(Chain([0, 1, 2, 3], [4, 5, 6, 7]));
        Assert.Equal([0, 1, 2, 3, 4, 5], reader.Read(6)); // straddles the seam, stitched into a pooled temp
        Assert.Equal([6, 7], reader.Read(2));
    }

    [Fact]
    public void FromByValueParameterWithScratch_ReadsAcrossSeam()
    {
        Span<byte> scratch = stackalloc byte[16];
        using var reader = new OwningReader(Chain([0, 1, 2, 3], [4, 5, 6, 7]), scratch);
        Assert.Equal([0, 1, 2, 3, 4, 5], reader.Read(6)); // stitched into scratch
        Assert.Equal([6, 7], reader.Read(2));
    }

    [Fact]
    public void FromLocal_EscapesFactory()
    {
        using var reader = OwningReader.FromLocal([0, 1, 2, 3], [4, 5, 6, 7]);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], reader.Read(8));
    }
}
