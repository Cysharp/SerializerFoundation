using System.IO.Pipelines;

namespace SerializerFoundation.Tests;

public class PipeWriterAsyncWriteBufferTest
{
    [Test]
    public async Task Basic_WriteAndAdvance()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        await buffer.EnsureBufferAsync(10, CancellationToken.None);

        buffer.TryGetSpan(10, out var span);
        span[0] = 0xAB;
        buffer.Advance(10);

        await Assert.That(buffer.BytesWritten).IsEqualTo(10);

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task TryGetSpan_ReturnsFalseWhenNoBuffer()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        // Before EnsureBufferAsync, TryGetSpan should return false
        var result = buffer.TryGetSpan(10, out var span);
        await Assert.That(result).IsFalse();

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task TryGetReference_ReturnsFalseWhenNoBuffer()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        byte reference = 0;
        var result = buffer.TryGetReference(10, ref reference);
        await Assert.That(result).IsFalse();

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task TryGetSpan_ReturnsTrueAfterEnsure()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        await buffer.EnsureBufferAsync(10, CancellationToken.None);

        var result = buffer.TryGetSpan(10, out var span);
        (result).IsEqualTo(true);
        span.Length.IsGreaterThanOrEqualTo(10);

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task TryGetReference_ReturnsTrueAfterEnsure()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        await buffer.EnsureBufferAsync(10, CancellationToken.None);

        byte reference = 0;
        var result = buffer.TryGetReference(10, ref reference);
        await Assert.That(result).IsTrue();

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task BytesWritten_InitiallyZero()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        await Assert.That(buffer.BytesWritten).IsEqualTo(0);

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task FlushAsync_WritesDataToPipe()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        await buffer.EnsureBufferAsync(4, CancellationToken.None);

        buffer.TryGetSpan(4, out var span);
        span[0] = 1;
        span[1] = 2;
        span[2] = 3;
        span[3] = 4;
        buffer.Advance(4);

        await buffer.FlushAsync(CancellationToken.None);

        // Read from pipe to verify
        var readResult = await pipe.Reader.ReadAsync();
        await Assert.That(readResult.Buffer.Length).IsEqualTo(4);
        await Assert.That(readResult.Buffer.FirstSpan[0]).IsEqualTo((byte)1);
        await Assert.That(readResult.Buffer.FirstSpan[1]).IsEqualTo((byte)2);
        await Assert.That(readResult.Buffer.FirstSpan[2]).IsEqualTo((byte)3);
        await Assert.That(readResult.Buffer.FirstSpan[3]).IsEqualTo((byte)4);

        pipe.Reader.AdvanceTo(readResult.Buffer.End);

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Test]
    public async Task MultipleWrites()
    {
        var pipe = new Pipe();
        var buffer = new PipeWriterAsyncWriteBuffer(pipe.Writer);

        for (int i = 0; i < 5; i++)
        {
            await buffer.EnsureBufferAsync(1, CancellationToken.None);
            buffer.TryGetSpan(1, out var span);
            span[0] = (byte)i;
            buffer.Advance(1);
        }

        await Assert.That(buffer.BytesWritten).IsEqualTo(5);

        await buffer.DisposeAsync();
        await pipe.Writer.CompleteAsync();
    }
}

public class PipeReaderAsyncReadBufferTest
{
    [Test]
    public async Task Basic_ReadAndAdvance()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(data);
        var pipeReader = PipeReader.Create(stream);

        var buffer = new PipeReaderAsyncReadBuffer(pipeReader);

        await buffer.EnsureBufferAsync(5, CancellationToken.None);

        buffer.TryGetSpan(5, out var span);
        (span[0]).IsEqualTo((byte)1);
        (span[1]).IsEqualTo((byte)2);

        buffer.Advance(2);
        await Assert.That(buffer.BytesConsumed).IsEqualTo(2);

        await buffer.DisposeAsync();
        await pipeReader.CompleteAsync();
    }

    [Test]
    public async Task TryGetSpan_ReturnsFalseWhenNoBuffer()
    {
        var stream = new MemoryStream(new byte[10]);
        var pipeReader = PipeReader.Create(stream);

        var buffer = new PipeReaderAsyncReadBuffer(pipeReader);

        var result = buffer.TryGetSpan(10, out var span);
        await Assert.That(result).IsFalse();

        await buffer.DisposeAsync();
        await pipeReader.CompleteAsync();
    }

    [Test]
    public async Task TryGetSpan_ReturnsTrueAfterEnsure()
    {
        var stream = new MemoryStream(new byte[100]);
        var pipeReader = PipeReader.Create(stream);

        var buffer = new PipeReaderAsyncReadBuffer(pipeReader);

        await buffer.EnsureBufferAsync(10, CancellationToken.None);

        var result = buffer.TryGetSpan(10, out var span);
        (result).IsEqualTo(true);
        (span.Length).IsGreaterThanOrEqualTo(10);

        await buffer.DisposeAsync();
        await pipeReader.CompleteAsync();
    }

    [Test]
    public async Task BytesConsumed_InitiallyZero()
    {
        var stream = new MemoryStream(new byte[100]);
        var pipeReader = PipeReader.Create(stream);

        var buffer = new PipeReaderAsyncReadBuffer(pipeReader);

        await Assert.That(buffer.BytesConsumed).IsEqualTo(0);

        await buffer.DisposeAsync();
        await pipeReader.CompleteAsync();
    }
}
