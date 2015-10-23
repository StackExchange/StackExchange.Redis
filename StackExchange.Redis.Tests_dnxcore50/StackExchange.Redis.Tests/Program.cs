using System;
using System.Reflection;
using NUnitLite;

namespace StackExchange.Redis.Tests
{
    public class Program
    {
        public int Main(string[] args)
        {
            return new AutoRun().Execute(typeof(AsyncTests).GetTypeInfo().Assembly, Console.Out, Console.In, args);
        }
    }
}