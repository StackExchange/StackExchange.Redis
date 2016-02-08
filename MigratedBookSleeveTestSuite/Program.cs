using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tests
{
    class Program
    {
        static void Main()
        {
            try
            {
                Main2();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("CRAZY ERRORS: " + ex);
            }
            finally
            {
                Console.WriteLine("Press any key to exit");
                Console.ReadKey();
            }
        }
        static void Main2()
        {
            // why is this here? because some dumbass forgot to install a decent test-runner before going to the airport
            var epicFail = new List<string>();
            var testTypes = from type in typeof(Program).Assembly.GetTypes()
                            where Attribute.IsDefined(type, typeof(TestFixtureAttribute))
                            && !Attribute.IsDefined(type, typeof(IgnoreAttribute))
                            let methods = type.GetMethods()
                            select new
                            {
                                Type = type,
                                Methods = methods,
                                ActiveMethods = methods.Where(x => Attribute.IsDefined(x, typeof(ActiveTestAttribute))).ToArray(),
                                Setup = methods.SingleOrDefault(x => Attribute.IsDefined(x, typeof(TestFixtureSetUpAttribute))),
                                TearDown = methods.SingleOrDefault(x => Attribute.IsDefined(x, typeof(TestFixtureTearDownAttribute)))
                            };
            int pass = 0, fail = 0;

            bool activeOnly = testTypes.SelectMany(x => x.ActiveMethods).Any();

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                args.SetObserved();
                //if (args.Exception is AggregateException)
                //{
                //    foreach (var ex in ((AggregateException)args.Exception).InnerExceptions)
                //    {
                //        Console.WriteLine(ex.Message);
                //    }
                //}
                //else
                //{
                //    Console.WriteLine(args.Exception.Message);
                //}
            };

            foreach (var type in testTypes)
            {
                var tests = (from method in (activeOnly ? type.ActiveMethods : type.Methods)
                             where Attribute.IsDefined(method, typeof(TestAttribute))
                             && !Attribute.IsDefined(method, typeof(IgnoreAttribute))
                             select method).ToArray();

                if (tests.Length == 0) continue;

                Console.WriteLine(type.Type.FullName);
                object obj;
                try
                {
                    obj = Activator.CreateInstance(type.Type);
                    if (obj == null) throw new InvalidOperationException("the world has gone mad");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    continue;
                }
                using (obj as IDisposable)
                {
                    if (type.Setup != null)
                    {
                        try
                        { type.Setup.Invoke(obj, null); }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Test fixture startup failed: " + ex.Message);
                            fail++;
                            epicFail.Add(type.Setup.DeclaringType.FullName + "." + type.Setup.Name);
                            continue;
                        }
                    }

                    foreach (var test in tests)
                    {
                        Console.Write(test.Name + ": ");
                        Exception err = null;

                        try
                        {
                            int count = 1;
                            if (activeOnly)
                            {
                                var ata = test.GetCustomAttribute(typeof(ActiveTestAttribute)) as ActiveTestAttribute;
                                if (ata != null) count = ata.Count;
                            }
                            while (count-- > 0)
                            {
                                test.Invoke(obj, null);
                            }
                        }
                        catch (TargetInvocationException ex)
                        {
                            err = ex.InnerException;
                        }
                        catch (Exception ex)
                        {
                            err = ex;
                        }

                        if (err is AggregateException && ((AggregateException)err).InnerExceptions.Count == 1)
                        {
                            err = ((AggregateException)err).InnerExceptions[0];
                        }

                        if (err == null)
                        {
                            Console.WriteLine("pass");
                            pass++;
                        }
                        else
                        {
                            Console.WriteLine(err.Message);
                            fail++;
                            epicFail.Add(test.DeclaringType.FullName + "." + test.Name);
                        }
                    }
                    if (type.TearDown != null)
                    {
                        try
                        { type.TearDown.Invoke(obj, null); }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Test fixture teardown failed: " + ex.Message);
                            fail++;
                            epicFail.Add(type.TearDown.DeclaringType.FullName + "." + type.TearDown.Name);
                        }
                    }
                }
            }
            Console.WriteLine("Passed: {0}; Failed: {1}", pass, fail);
            foreach (var msg in epicFail) Console.WriteLine(msg);
//#if DEBUG
//            Console.WriteLine();
//            Console.WriteLine("Callbacks: {0:###,###,##0} sync, {1:###,###,##0} async",
//                BookSleeve.RedisConnectionBase.AllSyncCallbacks, BookSleeve.RedisConnectionBase.AllAsyncCallbacks);
//#endif

        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ActiveTestAttribute : Attribute
{
    public int Count { get; }
    public ActiveTestAttribute() : this(1) { }
    public ActiveTestAttribute(int count) { this.Count = count; }
}