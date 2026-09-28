using System.Runtime.InteropServices;

namespace ZeroAlloc.Collections.AotSmoke;

/// <summary>A user-defined value type used as a dictionary key and a nullable collection element.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SmokeKey(int Id, int Part);
