using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

namespace SerializerFoundation;

// TODO: not implemented
public class PipeWriterAsyncWriteBuffer(PipeWriter pipeWriter) : IAsyncWriteBuffer
{
    // Span<byte> buffer;
    Memory<byte> buffer;

    public void Advance(int bytesConsumed)
    {
        throw new NotImplementedException();
    }

    public async ValueTask EnsureBufferAsync(int byteCount, CancellationToken cancellationToken)
    {
        await pipeWriter.FlushAsync(cancellationToken);

        buffer = pipeWriter.GetMemory(byteCount);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public bool TryGetSpan(int byteCount, out Span<byte> span)
    {
        if (buffer.Length < byteCount)
        {
            span = default;
            return false;
        }

        span = buffer.Span;
        return true;
    }
}
