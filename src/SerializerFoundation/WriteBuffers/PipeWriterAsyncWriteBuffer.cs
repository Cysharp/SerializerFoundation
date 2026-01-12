using System.IO.Pipelines;
using System.Threading;

namespace SerializerFoundation;

public sealed class PipeWriterAsyncWriteBuffer : IAsyncWriteBuffer
{
    PipeWriter pipeWriter;
    PointerSpan buffer;
    MemoryHandle bufferHandle;
    int writtenInBuffer;
    long totalWritten;

    public long BytesWritten => totalWritten;

    public PipeWriterAsyncWriteBuffer(PipeWriter pipeWriter)
    {
        this.pipeWriter = pipeWriter;
    }

    public bool TryGetSpan(int sizeHint, out Span<byte> span)
    {
        // TODO: when sizeHint is zero
        if ((uint)buffer.Length < (uint)sizeHint)
        {
            span = default;
            return false;
        }

        span = buffer;
        return true;
    }

    public bool TryGetReference(int sizeHint, ref byte reference)
    {
        if ((uint)buffer.Length < (uint)sizeHint)
        {
            return false;
        }

        reference = ref buffer.GetReference();
        return true;
    }

    public void Advance(int bytesWritten)
    {
        buffer.Advance(bytesWritten);
        writtenInBuffer += bytesWritten;
        totalWritten += bytesWritten;
    }

    public async ValueTask EnsureBufferAsync(int sizeHint, CancellationToken cancellationToken)
    {
        if (writtenInBuffer > 0)
        {
            await pipeWriter.FlushAsync(cancellationToken);
            writtenInBuffer = 0;
        }

        var memory = pipeWriter.GetMemory(sizeHint);
        unsafe
        {
            bufferHandle.Dispose();

            var handle = memory.Pin(); // keep pinned until next Flush
            this.bufferHandle = handle;
            this.buffer = new PointerSpan((byte*)handle.Pointer, memory.Length);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (writtenInBuffer > 0)
        {
            await pipeWriter.FlushAsync(CancellationToken.None);
            writtenInBuffer = 0;
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (writtenInBuffer > 0)
            {
                await pipeWriter.FlushAsync(cancellationToken);
                writtenInBuffer = 0;
            }
        }
        finally
        {
            bufferHandle.Dispose();
        }
    }
}
