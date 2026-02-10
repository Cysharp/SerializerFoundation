using System.Buffers;

namespace SerializerFoundation.Tests;

public class NonRefArrayPoolWriteBufferTest
{
    [Test]
    public void Basic_WriteAndAdvance()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
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
        }
    }

    [Test]
    public void GetReference_Basic()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
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
        }
    }

    [Test]
    public void BytesWritten_InitiallyZero()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
                try
                {
                    buffer.BytesWritten.IsEqualTo(0);
                }
                finally
                {
                    buffer.Dispose();
                }
            }
        }
    }

    [Test]
    public void ToArray_EmptyBuffer()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
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
        }
    }

    [Test]
    public async Task ToArray_WithData()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
                try
                {
                    var span = buffer.GetSpan(4);
                    span[0] = 1;
                    span[1] = 2;
                    span[2] = 3;
                    span[3] = 4;
                    buffer.Advance(4);

                    var result = buffer.ToArray();
                    (result).IsEquivalentTo(new byte[] { 1, 2, 3, 4 });
                }
                finally
                {
                    buffer.Dispose();
                }
            }
        }
    }

    [Test]
    public void AutoExpand_ExceedsScratchBuffer()
    {
        byte[] scratch = new byte[64]; // Small scratch buffer
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
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
        }
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);

                buffer.GetSpan(100);
                buffer.Advance(100);

                buffer.Dispose();
                buffer.Dispose(); // Should not throw
            }
        }
    }

    [Test]
    public void Flush_DoesNotThrow()
    {
        byte[] scratch = new byte[256];
        unsafe
        {
            fixed (byte* ptr = scratch)
            {
                var buffer = new NonRefArrayPoolListWriteBuffer(ptr, scratch.Length);
                try
                {
                    buffer.GetSpan(10);
                    buffer.Advance(10);
                    buffer.Flush(); // Should not throw (no-op)
                }
                finally
                {
                    buffer.Dispose();
                }
            }
        }
    }
}
