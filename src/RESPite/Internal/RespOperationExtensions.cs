using System.Runtime.CompilerServices;

namespace RESPite.Internal;

public static class RespOperationExtensions
{
#if PREVIEW_LANGVER
    extension<T>(in RespOperation<T> operation)
    {
        // since this is valid...
        public ref readonly RespOperation<T> Self => ref operation;

        // so is this (the types are layout-identical)
        public ref readonly RespOperation Untyped => ref Unsafe.As<RespOperation<T>, RespOperation>(
            ref Unsafe.AsRef(in operation));
    }
#endif
}
