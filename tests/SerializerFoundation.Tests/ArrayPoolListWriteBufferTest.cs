using System.Buffers;

namespace SerializerFoundation.Tests;

public class ArrayPoolListWriteBufferTest
{
    [Test]
    public void Basic_WriteAndAdvance()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            var span = buffer.GetSpan(10);
            span[0] = 0xAB;
            buffer.Advance(10);

            buffer.BytesWritten.IsEqualTo(10);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void GetReference_Basic()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            ref byte reference = ref buffer.GetReference(1);
            reference = 0xCD;
            buffer.Advance(1);

            buffer.BytesWritten.IsEqualTo(1);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void BytesWritten_InitiallyZero()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            buffer.BytesWritten.IsEqualTo(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void ToArray_EmptyBuffer()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            var result = buffer.ToArray();
            result.Length.IsEqualTo(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void ToArray_WithData()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            var span = buffer.GetSpan(4);
            span[0] = 1;
            span[1] = 2;
            span[2] = 3;
            span[3] = 4;
            buffer.Advance(4);

            var result = buffer.ToArray();
            result.IsEquivalentTo(new byte[] { 1, 2, 3, 4 });
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task AutoExpand_ExceedsScratchBuffer()
    {
        Span<byte> scratch = stackalloc byte[64]; // Small scratch buffer
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            // Fill scratch buffer
            var span1 = buffer.GetSpan(64);
            buffer.Advance(64);

            // Request more space - should auto-expand from ArrayPool
            var span2 = buffer.GetSpan(100);

            span2.Length.IsGreaterThanOrEqualTo(100);

            buffer.Advance(100);
            buffer.BytesWritten.IsEqualTo(164);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task AutoExpand_ToArray_PreservesAllData()
    {
        Span<byte> scratch = stackalloc byte[64];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            // Write to scratch buffer
            var span1 = buffer.GetSpan(64);
            for (int i = 0; i < 64; i++) span1[i] = (byte)i;
            buffer.Advance(64);

            // Write to pooled buffer
            var span2 = buffer.GetSpan(64);
            for (int i = 0; i < 64; i++) span2[i] = (byte)(64 + i);
            buffer.Advance(64);

            var result = buffer.ToArray();
            result.Length.IsEqualTo(128);

            // Verify all data preserved
            for (int i = 0; i < 128; i++)
            {
                result[i].IsEqualTo((byte)i);
            }
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void WriteTo_Destination()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            var span = buffer.GetSpan(4);
            span[0] = 10;
            span[1] = 20;
            span[2] = 30;
            span[3] = 40;
            buffer.Advance(4);

            Span<byte> dest = stackalloc byte[10];
            buffer.WriteTo(dest);

            dest[0].IsEqualTo((byte)10);
            dest[1].IsEqualTo((byte)20);
            dest[2].IsEqualTo((byte)30);
            dest[3].IsEqualTo((byte)40);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void MultipleSegments_LargeWrite()
    {
        Span<byte> scratch = stackalloc byte[32];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            // Write across multiple segments
            int totalWritten = 0;
            for (int seg = 0; seg < 3; seg++)
            {
                var span = buffer.GetSpan(65536); // Request 64KB
                for (int i = 0; i < 1000; i++)
                {
                    span[i] = (byte)(seg * 10);
                }
                buffer.Advance(1000);
                totalWritten += 1000;
            }

            buffer.BytesWritten.IsEqualTo(totalWritten);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);

        buffer.GetSpan(100);
        buffer.Advance(100);

        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }

    [Test]
    public void Flush_DoesNotThrow()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            buffer.GetSpan(10);
            buffer.Advance(10);
            buffer.Flush(); // Should not throw (no-op for this buffer)
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task SizeHint_Zero_ReturnsNonEmptyBuffer()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            var span = buffer.GetSpan(0);
            span.Length.IsGreaterThan(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void IntBytesWritten_Extension()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);
        try
        {
            buffer.Advance(42);
            buffer.IntBytesWritten.IsEqualTo(42);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void Iterate()
    {
        Span<byte> scratch = stackalloc byte[256];
        var buffer = new ArrayPoolListWriteBuffer(scratch);

        buffer.GetSpan(100);
        buffer.Advance(100);

        buffer.GetSpan(1000);
        buffer.Advance(1000);

        buffer.GetSpan(10000);
        buffer.Advance(10000);

        buffer.GetSpan(100000);
        buffer.Advance(100000);

        var len = 0;
        var segments = buffer.GetWrittenSegments();
        while (segments.TryGetNext(out var segment))
        {
            // Process each segment
            len += segment.Length;
        }

        len.IsEqualTo(buffer.IntBytesWritten);
    }

    [Test]
    public unsafe void Iterate2()
    {
        Span<byte> scratch = stackalloc byte[256];
        fixed (byte* p = scratch)
        {
            var buffer = new NonRefArrayPoolListWriteBuffer(p, scratch.Length);

            buffer.GetSpan(100);
            buffer.Advance(100);

            buffer.GetSpan(1000);
            buffer.Advance(1000);

            buffer.GetSpan(10000);
            buffer.Advance(10000);

            buffer.GetSpan(100000);
            buffer.Advance(100000);

            var len = 0;
            var segments = buffer.GetWrittenSegments();
            while (segments.TryGetNext(out var segment))
            {
                // Process each segment
                len += segment.Length;
            }

            len.IsEqualTo(buffer.IntBytesWritten);
        }
    }
}
