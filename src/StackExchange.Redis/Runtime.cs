using System;
using System.Runtime.InteropServices;

namespace StackExchange.Redis;

internal static class Runtime
{
    public static readonly bool IsMono = RuntimeInformation.FrameworkDescription.StartsWith("Mono ", StringComparison.OrdinalIgnoreCase);
}
