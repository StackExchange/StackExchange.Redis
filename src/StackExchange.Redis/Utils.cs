using System;
using System.Reflection;

namespace StackExchange.Redis;

internal static class Utils
{
    private static string? _libVersion;
    internal static string GetLibVersion()
    {
        if (_libVersion == null)
        {
            var assembly = typeof(ConnectionMultiplexer).Assembly;
            _libVersion = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyFileVersionAttribute))!)?.Version
                ?? assembly.GetName().Version!.ToString();
        }
        return _libVersion;
    }
}
