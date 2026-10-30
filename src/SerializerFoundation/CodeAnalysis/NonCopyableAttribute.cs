namespace SerializerFoundation.CodeAnalysis;

/// <summary>
/// Marks a struct as single-owner mutable state that must not be copied, which the bundled analyzer enforces as SF002.
/// Buffer structs need no marker: implementing <see cref="IWriteBuffer"/> or <see cref="IReadBuffer"/> already opts them in.
/// Apply it to a struct that is not a buffer itself but holds one by value, such as a serializer's reader or writer,
/// where a copy duplicates the inner buffer just as copying the buffer would.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class NonCopyableAttribute : Attribute
{
}
