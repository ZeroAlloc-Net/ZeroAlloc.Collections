namespace ZeroAlloc.Collections;

/// <summary>
/// Generates a zero-allocation ref struct enumerator for types with an array field and an int count field.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ZeroAllocEnumerableAttribute : Attribute
{
}
