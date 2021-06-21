using System;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Helpers
{
    public static class Extensions
    {
        private static string VersionInfo { get; }

        static Extensions()
        {
#if NET462
            VersionInfo = "Compiled under .NET 4.6.2";
#else
            VersionInfo = $"Running under {RuntimeInformation.FrameworkDescription} ({Environment.Version})";
#endif
            try
            {
                VersionInfo += "\n   Running on: " + RuntimeInformation.OSDescription;
            }
            catch (Exception)
            {
                VersionInfo += "\n   Failed to get OS version";
            }
        }

        public static void WriteFrameworkVersion(this ITestOutputHelper output) => output.WriteLine(VersionInfo);
    }
}
