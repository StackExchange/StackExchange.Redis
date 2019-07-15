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
            VersionInfo = "Compiled under .NET 4.6.1";
#elif NETCOREAPP2_2
            VersionInfo = "Compiled under .NETCoreApp2.2";
#else
            VersionInfo = "Compiled under <unknown framework>";
#endif
            try
            {
                VersionInfo += "\nRunning on: " + RuntimeInformation.OSDescription;
            }
            catch (Exception)
            {
                VersionInfo += "\nFailed to get OS version";
            }
        }

        public static void WriteFrameworkVersion(this ITestOutputHelper output) => output.WriteLine(VersionInfo);
    }
}
