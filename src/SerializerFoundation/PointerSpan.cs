namespace SerializerFoundation;

// Span like structure for a pointer and length.
// But mutable slice to optimize without JIT intrinsics of Span.
internal unsafe struct PointerSpan
{
    byte* pointer;
    int length;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => length;
    }
     
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PointerSpan(byte* pointer, int length)
    {
        this.pointer = pointer;
        this.length = length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetReference()
    {
        return ref Unsafe.AsRef<byte>(pointer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan()
    {
        return new Span<byte>(pointer, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        return new ReadOnlySpan<byte>(pointer, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Span<byte>(PointerSpan span)
    {
        return span.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<byte>(PointerSpan span)
    {
        return span.AsReadOnlySpan();
    }

    // like Slice but mutable.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int start)
    {
        if ((uint)start > (uint)Length) Throws.ArgumentOutOfRange();

        this.pointer = pointer + start;
        this.length = length - start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int start, int length)
    {
        if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)Length)
        {
            Throws.ArgumentOutOfRange();
        }

        this.pointer = pointer + start;
        this.length = length;
    }
}
