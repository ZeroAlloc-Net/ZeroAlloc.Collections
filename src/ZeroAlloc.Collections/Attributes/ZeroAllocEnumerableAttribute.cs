namespace ZeroAlloc.Collections;

/// <summary>
/// Generates a zero-allocation ref struct enumerator for the decorated type.
/// </summary>
/// <remarks>
/// The generator locates the backing array and element count by inspecting the type's fields.
/// If the type has multiple array fields or multiple <c>int</c> fields, use the overload that
/// accepts explicit field names to avoid ambiguity.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ZeroAllocEnumerableAttribute : Attribute
{
    /// <summary>
    /// Marks the type for zero-allocation enumerator generation using field auto-discovery.
    /// </summary>
    public ZeroAllocEnumerableAttribute() { }

    /// <summary>
    /// Marks the type for zero-allocation enumerator generation with explicit field names.
    /// </summary>
    /// <param name="arrayFieldName">The name of the backing array field (e.g. <c>"_items"</c>).</param>
    /// <param name="countFieldName">The name of the count field (e.g. <c>"_count"</c>).</param>
    public ZeroAllocEnumerableAttribute(string arrayFieldName, string countFieldName)
    {
        ArrayFieldName = arrayFieldName;
        CountFieldName = countFieldName;
    }

    /// <summary>Gets the explicit backing array field name, or <c>null</c> for auto-discovery.</summary>
    public string? ArrayFieldName { get; }

    /// <summary>Gets the explicit count field name, or <c>null</c> for auto-discovery.</summary>
    public string? CountFieldName { get; }
}
