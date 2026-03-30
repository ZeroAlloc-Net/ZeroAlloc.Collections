namespace ZeroAlloc.Collections;

/// <summary>
/// Generates a strongly-typed pooled collection wrapper with automatic dispose/return-to-pool logic.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class PooledCollectionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="PooledCollectionAttribute"/> with the specified element type.
    /// </summary>
    /// <param name="elementType">The type of elements in the generated collection.</param>
    public PooledCollectionAttribute(Type elementType) => ElementType = elementType;

    /// <summary>
    /// Gets the type of elements in the generated collection.
    /// </summary>
    public Type ElementType { get; }
}
