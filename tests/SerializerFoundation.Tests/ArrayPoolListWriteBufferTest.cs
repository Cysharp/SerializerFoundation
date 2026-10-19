namespace SerializerFoundation.Tests;

// ArrayPoolListWriteBuffer bookkeeping across the scratch span and the rented segment table:
// BytesWritten / ToArray / WriteTo agree on every path (scratch only, one spill, several
// spills), and a disposed buffer starts over cleanly when it is written to again.
public class ArrayPoolListWriteBufferTest
{
    static byte[] WriteChunks(ref ArrayPoolListWriteBuffer buffer, int total, int chunk)
    {
        var expected = new byte[total];
        var written = 0;
        var seed = 0;
        while (written < total)
        {
            var size = Math.Min(chunk, total - written);
            var span = buffer.GetSpan(size);
            for (var i = 0; i < size; i++)
            {
                var b = (byte)(seed++ * 31);
                span[i] = b;
                expected[written + i] = b;
            }
            buffer.Advance(size);
            written += size;
        }
        return expected;
    }

    [Theory]
    [InlineData(0)]        // nothing written
    [InlineData(1)]        // scratch only
    [InlineData(32)]       // scratch exactly full, no spill
    [InlineData(33)]       // first spill, one pooled segment
    [InlineData(70_000)]   // past the 64KB first pooled segment: two pooled segments
    [InlineData(300_000)]  // three pooled segments (64KB, 128KB, 256KB)
    public void ToArray_WriteTo_BytesWritten_Agree(int total)
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var expected = WriteChunks(ref buffer, total, 77);

            Assert.Equal(total, buffer.BytesWritten);
            Assert.Equal(expected, buffer.ToArray());

            var destination = new byte[total + 8];
            buffer.WriteTo(destination);
            Assert.Equal(expected, destination.AsSpan(0, total).ToArray());
            Assert.All(destination.AsSpan(total).ToArray(), static b => Assert.Equal(0, b));

            var segments = buffer.GetWrittenSegments();
            Assert.Equal(total, segments.Length);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void ToArray_EmptyScratch_SpillsFromTheFirstByte()
    {
        var buffer = new ArrayPoolListWriteBuffer(Span<byte>.Empty);
        try
        {
            var expected = WriteChunks(ref buffer, 100_000, 1000);
            Assert.Equal(100_000, buffer.BytesWritten);
            Assert.Equal(expected, buffer.ToArray());

            // the scratch span contributed nothing, so it is not a segment
            var segments = buffer.GetWrittenSegments();
            Assert.Equal(2, segments.SegmentCount);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Dispose_ResetsEveryCounter_AndTheBufferCanBeReused()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            WriteChunks(ref buffer, 150_000, 500);
            Assert.Equal(150_000, buffer.BytesWritten);

            buffer.Dispose();
            Assert.Equal(0, buffer.BytesWritten);
            Assert.Empty(buffer.ToArray());
            var empty = buffer.GetWrittenSegments();
            Assert.Equal(0, empty.SegmentCount);

            // Dispose is idempotent
            buffer.Dispose();

            // writing again starts a fresh chain (currentBuffer was dropped, so this goes straight to the pool)
            var expected = WriteChunks(ref buffer, 70_000, 64);
            Assert.Equal(70_000, buffer.BytesWritten);
            Assert.Equal(expected, buffer.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Compatible_ToArray_WriteTo_BytesWritten_Agree()
    {
        var buffer = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            var expected = new byte[300_000];
            new Random(11).NextBytes(expected);
            var written = 0;
            while (written < expected.Length)
            {
                var size = Math.Min(777, expected.Length - written);
                expected.AsSpan(written, size).CopyTo(buffer.GetSpan(size));
                buffer.Advance(size);
                written += size;
            }

            Assert.Equal(expected.Length, buffer.BytesWritten);
            Assert.Equal(expected, buffer.ToArray());

            var destination = new byte[expected.Length];
            buffer.WriteTo(destination);
            Assert.Equal(expected, destination);

            buffer.Dispose();
            Assert.Equal(0, buffer.BytesWritten);
            Assert.Empty(buffer.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
