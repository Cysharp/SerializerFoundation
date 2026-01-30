using System.IO.Pipelines;

namespace SerializerFoundation;

public struct PipeReaderAsyncReadBuffer : IAsyncReadBuffer
{
    PipeReader pipeReader;
    ReadOnlySequence<byte> sequence;
    PointerSpan buffer;
    MemoryHandle bufferHandle;
    byte[]? tempBuffer;
    int readInBuffer;
    long totalConsumed;

    public long BytesConsumed => totalConsumed;

    public PipeReaderAsyncReadBuffer(PipeReader pipeReader)
    {
        this.pipeReader = pipeReader;
    }

    public bool TryGetSpan(int sizeHint, out ReadOnlySpan<byte> span)
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

    public void Advance(int bytesConsumed)
    {
        buffer.Advance(bytesConsumed);
        readInBuffer += bytesConsumed;
        totalConsumed += bytesConsumed;
    }

    public async ValueTask EnsureBufferAsync(int sizeHint, CancellationToken cancellationToken)
    {
        ReturnTempBuffer();

        if (readInBuffer > 0)
        {
            pipeReader.AdvanceTo(sequence.GetPosition(readInBuffer));
            readInBuffer = 0;
        }

        var readResult = await pipeReader.ReadAtLeastAsync(sizeHint, cancellationToken);
        // TODO: check IsCompleted and result's length satisfy sizeHint?

        sequence = readResult.Buffer;
        var memory = sequence.First;

        if ((uint)memory.Length < (uint)sizeHint)
        {
            if ((uint)sequence.Length < (uint)sizeHint)
            {
                Throws.InsufficientSpaceInBuffer();
            }

            tempBuffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            sequence.Slice(0, sizeHint).CopyTo(tempBuffer);
            SetSpan(tempBuffer.AsMemory(0, sizeHint));
        }
        else
        {
            SetSpan(memory);
        }
    }

    unsafe void SetSpan(ReadOnlyMemory<byte> memory)
    {
        this.bufferHandle.Dispose(); // unpin previous
        var handle = memory.Pin();
        this.bufferHandle = handle;
        this.buffer = new PointerSpan((byte*)handle.Pointer, memory.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ReturnTempBuffer()
    {
        if (tempBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
            tempBuffer = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        ReturnTempBuffer();
        buffer = default;
        bufferHandle.Dispose();
        return default;
    }
}
