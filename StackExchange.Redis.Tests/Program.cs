#if NUNITLITE
using System;
using System.Reflection;
using NUnitLite;

namespace StackExchange.Redis.Tests
{
    public class Program
    {
        public int Main(string[] args)
        {
            return new AutoRun().Execute(typeof(TestBase).GetTypeInfo().Assembly, Console.Out, Console.In, args);
        }
    }
}
#endif