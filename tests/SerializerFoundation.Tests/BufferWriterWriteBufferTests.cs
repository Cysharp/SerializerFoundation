using System.Buffers;
using Xunit;

namespace SerializerFoundation.Tests;

// pins the index-representation rewrite (Advance = single add, total accumulated at Flush):
// writes cross several underlying segments (small initial ArrayBufferWriter), and
// BytesWritten must be exact both mid-write (lazy total) and after Flush
public class BufferWriterWriteBufferTests
{
    static byte[] ExpectedBytes(int count) => [.. Enumerable.Range(0, count).Select(i => (byte)i)];

    [Fact]
    public void NonGeneric_CrossesSegments_AndCountsLazily()
    {
        var inner = new ArrayBufferWriter<byte>(16);
        var b = new BufferWriterWriteBuffer(inner);

        for (int i = 0; i < 100; i++)
        {
            b.GetReference(1) = (byte)i;
            b.Advance(1);
            Assert.Equal(i + 1, b.BytesWritten); // mid-write, before any explicit Flush
        }

        b.Flush();
        Assert.Equal(100, b.BytesWritten); // unchanged across the flush boundary
        b.Dispose();

        Assert.Equal(ExpectedBytes(100), inner.WrittenSpan.ToArray());
    }

    [Fact]
    public void NonGeneric_BulkThenSmall_RespectsSizeHint()
    {
        var inner = new ArrayBufferWriter<byte>(16);
        var b = new BufferWriterWriteBuffer(inner);

        // reservation larger than the current segment forces the slow path
        var span = b.GetSpan(64);
        Assert.True(span.Length >= 64);
        for (int i = 0; i < 64; i++) span[i] = (byte)i;
        b.Advance(64);

        b.GetReference(1) = 64;
        b.Advance(1);

        b.Dispose(); // Dispose alone must flush
        Assert.Equal(ExpectedBytes(65), inner.WrittenSpan.ToArray());
    }

    [Fact]
    public void FailurePath_DisposeCommitsWhatWasWritten()
    {
        // the documented contract: bytes handed to an IBufferWriter cannot be taken back, and Dispose commits what
        // is still staged even when the formatter threw, so the writer holds a truncated message afterwards
        var inner = new ArrayBufferWriter<byte>(16);
        Assert.Throws<InvalidOperationException>(() => WriteThenFail(inner));
        Assert.Equal(ExpectedBytes(3), inner.WrittenSpan.ToArray());

        var compatibleInner = new ArrayBufferWriter<byte>(16);
        Assert.Throws<InvalidOperationException>(() => WriteThenFailCompatible(compatibleInner));
        Assert.Equal(ExpectedBytes(3), compatibleInner.WrittenSpan.ToArray());

        static void WriteThenFail(IBufferWriter<byte> output)
        {
            var b = new BufferWriterWriteBuffer(output);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    b.GetReference(1) = (byte)i;
                    b.Advance(1);
                }
                throw new InvalidOperationException("formatter failed");
            }
            finally
            {
                b.Dispose();
            }
        }

        static void WriteThenFailCompatible(IBufferWriter<byte> output)
        {
            var b = new CompatibleBufferWriterWriteBuffer(output);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    b.GetReference(1) = (byte)i;
                    b.Advance(1);
                }
                throw new InvalidOperationException("formatter failed");
            }
            finally
            {
                b.Dispose();
            }
        }
    }

}
