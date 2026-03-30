namespace ZeroAlloc.Collections;

/// <summary>
/// Generates a specialized, zero-allocation list implementation optimized for the specified type.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ZeroAllocListAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="ZeroAllocListAttribute"/> with the specified element type.
    /// </summary>
    /// <param name="elementType">The type of elements in the generated list.</param>
    public ZeroAllocListAttribute(Type elementType) => ElementType = elementType;

    /// <summary>
    /// Gets the type of elements in the generated list.
    /// </summary>
    public Type ElementType { get; }
}
