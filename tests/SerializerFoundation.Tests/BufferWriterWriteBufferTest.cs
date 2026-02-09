using System.Buffers;

namespace SerializerFoundation.Tests;

#if NET9_0_OR_GREATER

public class BufferWriterWriteBufferTest
{
    [Test]
    public async Task Basic_WriteAndAdvance()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref writer);
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
    public async Task GetReference_Basic()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref writer);
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
    public async Task BytesWritten_InitiallyZero()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref writer);
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
    public async Task Flush_AdvancesUnderlyingWriter()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref writer);

        var span = buffer.GetSpan(4);
        span[0] = 1;
        span[1] = 2;
        span[2] = 3;
        span[3] = 4;
        buffer.Advance(4);

        // Before flush, underlying writer may not have advanced
        buffer.Flush();

        // After flush, underlying writer should have the data
        (writer.WrittenCount).IsEqualTo(4);

        buffer.Dispose();
    }

    [Test]
    public async Task Dispose_FlushesAutomatically()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref writer);

        var span = buffer.GetSpan(4);
        span[0] = 10;
        span[1] = 20;
        span[2] = 30;
        span[3] = 40;
        buffer.Advance(4);

        buffer.Dispose();

        // After dispose, data should be flushed to underlying writer
        (writer.WrittenCount).IsEqualTo(4);
        (writer.WrittenSpan.ToArray()).IsEquivalentTo(new byte[] { 10, 20, 30, 40 });
    }

    [Test]
    public async Task MultipleWrites()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref writer);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                var span = buffer.GetSpan(1);
                span[0] = (byte)i;
                buffer.Advance(1);
            }

            (buffer.BytesWritten).IsEqualTo(10);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}

#endif

public class NonRefBufferWriterWriteBufferTest
{
    [Test]
    public async Task Basic_WriteAndAdvance()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new NonRefBufferWriterWriteBuffer(writer);
        try
        {
            var span = buffer.GetSpan(10);
            span[0] = 0xAB;
            buffer.Advance(10);

            (buffer.BytesWritten).IsEqualTo(10);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task GetReference_Basic()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new NonRefBufferWriterWriteBuffer(writer);
        try
        {
            ref byte reference = ref buffer.GetReference(1);
            reference = 0xCD;
            buffer.Advance(1);

            await Assert.That(buffer.BytesWritten).IsEqualTo(1);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task BytesWritten_InitiallyZero()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new NonRefBufferWriterWriteBuffer(writer);
        try
        {
            await Assert.That(buffer.BytesWritten).IsEqualTo(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task Flush_AdvancesUnderlyingWriter()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new NonRefBufferWriterWriteBuffer(writer);

        var span = buffer.GetSpan(4);
        span[0] = 1;
        span[1] = 2;
        span[2] = 3;
        span[3] = 4;
        buffer.Advance(4);

        buffer.Flush();

        await Assert.That(writer.WrittenCount).IsEqualTo(4);

        buffer.Dispose();
    }

    [Test]
    public async Task Dispose_FlushesAutomatically()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new NonRefBufferWriterWriteBuffer(writer);

        var span = buffer.GetSpan(4);
        span[0] = 10;
        span[1] = 20;
        span[2] = 30;
        span[3] = 40;
        buffer.Advance(4);

        buffer.Dispose();

        await Assert.That(writer.WrittenCount).IsEqualTo(4);
        await Assert.That(writer.WrittenSpan.ToArray()).IsEquivalentTo(new byte[] { 10, 20, 30, 40 });
    }

    [Test]
    public async Task MultipleWrites()
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new NonRefBufferWriterWriteBuffer(writer);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                var span = buffer.GetSpan(1);
                span[0] = (byte)i;
                buffer.Advance(1);
            }

            await Assert.That(buffer.BytesWritten).IsEqualTo(10);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
