using System.Buffers;

namespace SerializerFoundation.Tests;

// Helper to build multi-segment ReadOnlySequence for testing
internal sealed class MemorySegment<T> : ReadOnlySequenceSegment<T>
{
    public MemorySegment(ReadOnlyMemory<T> memory)
    {
        Memory = memory;
    }

    public MemorySegment<T> Append(ReadOnlyMemory<T> memory)
    {
        var segment = new MemorySegment<T>(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = segment;
        return segment;
    }
}

internal static class SequenceHelper
{
    // Creates a ReadOnlySequence split into segments of the given sizes
    public static ReadOnlySequence<byte> CreateMultiSegment(byte[] data, params int[] segmentSizes)
    {
        if (segmentSizes.Length == 0) throw new ArgumentException("Need at least one segment size");

        var offset = 0;
        var first = new MemorySegment<byte>(data.AsMemory(offset, segmentSizes[0]));
        offset += segmentSizes[0];

        var current = first;
        for (int i = 1; i < segmentSizes.Length; i++)
        {
            current = current.Append(data.AsMemory(offset, segmentSizes[i]));
            offset += segmentSizes[i];
        }

        return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
    }
}
