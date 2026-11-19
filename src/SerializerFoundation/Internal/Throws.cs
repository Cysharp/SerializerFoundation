using System.Diagnostics.CodeAnalysis;

namespace SerializerFoundation;

internal static class Throws
{
    [DoesNotReturn]
    internal static void ArgumentOutOfRange() => throw new ArgumentOutOfRangeException();

    [DoesNotReturn]
    internal static void ArgumentNull(string? paramName) => throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    internal static void AdvancedTooFar() => throw new InvalidOperationException("Cannot advance past the end of the buffer.");

    [DoesNotReturn]
    internal static void InsufficientSpaceInBuffer() => throw new InvalidOperationException("Insufficient space in buffer.");

    // read-side twin of InsufficientSpaceInBuffer, for a CopyTo destination longer than the remaining data
    [DoesNotReturn]
    internal static void InsufficientDataInBuffer() => throw new InvalidOperationException("Insufficient data in buffer.");

    // ToArray on a message longer than the largest array: the segments still hold all of it
    [DoesNotReturn]
    internal static void MessageTooLargeForArray() => throw new InvalidOperationException("The written message is too large to copy into a single array. Read it through GetWrittenSegments instead.");

    // a pooled-list write buffer that still holds its segment table after the table went back to the pool,
    // which only a copy of the buffer can cause: the other copy was disposed
    [DoesNotReturn]
    internal static void PooledStorageReturned(string objectName) => throw new ObjectDisposedException(objectName, "The buffer's pooled storage has already been returned. The buffer was copied and one of the copies was disposed.");

    // BufferSegments used after the buffer it was taken from was disposed
    [DoesNotReturn]
    internal static void SegmentsOutlivedBuffer() => throw new ObjectDisposedException(nameof(BufferSegments), "The buffer these segments were taken from has been disposed.");

    [DoesNotReturn]
    internal static T InsufficientSpaceInBuffer<T>() => throw new InvalidOperationException("Insufficient space in buffer.");
}
