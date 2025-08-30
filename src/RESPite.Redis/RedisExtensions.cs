using System.Runtime.CompilerServices;

namespace RESPite.Redis;

public static class RedisExtensions
{
#if PREVIEW_LANGVER
    extension(in RespContext context)
    {
        // since this is valid...
        // public ref readonly RespContext Self => ref context;

        // so must this be (importantly, RedisStrings has only a single RespContext field)
        public ref readonly RedisStrings Strings
            => ref Unsafe.As<RespContext, RedisStrings>(ref Unsafe.AsRef(in context));

        public ref readonly RedisKeys Keys
            => ref Unsafe.As<RespContext, RedisKeys>(ref Unsafe.AsRef(in context));
    }
#endif
}
