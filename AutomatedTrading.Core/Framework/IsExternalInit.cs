// Polyfill required for C# 9 records and init-only setters when targeting .NET Framework 4.8.
// System.Runtime.CompilerServices.IsExternalInit ships with .NET 5+; for net48 we define
// the empty marker class so the compiler is satisfied.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
