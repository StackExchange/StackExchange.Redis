#pragma warning disable SA1403 // single namespace

#if !NET9_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // see https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.overloadresolutionpriorityattribute
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
    {
        public int Priority => priority;
    }
}
#endif

#pragma warning restore SA1403
