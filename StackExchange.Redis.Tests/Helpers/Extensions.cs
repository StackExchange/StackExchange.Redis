using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Helpers
{
    public static class Extensions
    {
        public static void WriteFrameworkVersion(this ITestOutputHelper output)
        {
#if NET462
            output.WriteLine("Compiled under .NET 4.6.2");
#elif NETCOREAPP1_0
            output.WriteLine("Compiled under .NETCoreApp1.0");
#elif NETCOREAPP2_0
            output.WriteLine("Compiled under .NETCoreApp2.0");
#else
            output.WriteLine("Compiled under <unknown framework>");
#endif
            output.WriteLine("Running on: " + RuntimeInformation.OSDescription);
        }
    }
}
