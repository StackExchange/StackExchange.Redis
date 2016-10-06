#if NUNITLITE && !CORE_CLR
using System;
using System.Reflection;
using NUnit.Common;
using NUnitLite;

namespace StackExchange.Redis.Tests
{
    public class Program
    {
        public static int Main(string[] args)
        {
            return new AutoRun(typeof(TestBase).GetTypeInfo().Assembly)
                .Execute(args, new ExtendedTextWrapper(Console.Out), Console.In);
        }
    }
}
#endif