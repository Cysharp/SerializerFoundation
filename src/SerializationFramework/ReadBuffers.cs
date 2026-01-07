using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

namespace SerializationFramework;

// TODO: not implemented

public ref struct ReadOnlySpanBuffer : IReadBuffer
{
    ReadOnlySpan<byte> buffer;

    public ReadOnlySpanBuffer(ReadOnlySpan<byte> buffer)
    {
        this.buffer = buffer;
    }

    public ReadOnlySpan<byte> GetSpan(int byteCount)
    {
        if (buffer.Length < byteCount)
        {
            throw new InvalidOperationException("Insufficient data in buffer.");
        }

        return buffer;
    }

    public void Advance(int bytesConsumed)
    {
        buffer = buffer.Slice(bytesConsumed);
    }
}


public class PipeWriterBuffer(PipeWriter pipeWriter) : IAsyncWriteBuffer
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
