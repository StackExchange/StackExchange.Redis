using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Utility to detect continuations on tasks
    /// </summary>
    public static class TaskContinationCheck
    {
        static TaskContinationCheck()
        {
            NoContinuations = task => false; // assume the worst, hope for the best
            try
            {
                var field = typeof(Task).GetField("m_continuationObject", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    System.Diagnostics.Trace.WriteLine("Expected field not found: Task.m_continuationObject");
                    return;
                }

                var method = new DynamicMethod("NoContinuations", typeof(bool), new[] { typeof(Task) },
                    typeof(Task), true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldflda, field);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.EmitCall(OpCodes.Call, typeof(Interlocked).GetMethod("CompareExchange", new[] { typeof(object).MakeByRefType(), typeof(object), typeof(object) }), null);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ret);

                var func = (Func<Task, bool>)method.CreateDelegate(typeof(Func<Task, bool>));
                TaskCompletionSource<int> source = new TaskCompletionSource<int>();
                var before = func(source.Task);
                source.Task.ContinueWith(t => { });
                var after = func(source.Task);
                if (!before)
                {
                    System.Diagnostics.Trace.WriteLine("vanilla task should report true");
                    return;
                }
                if (after)
                {
                    System.Diagnostics.Trace.WriteLine("task with continuation should report false");
                    return;
                }
                source.TrySetResult(0);
                NoContinuations = func;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }
        }
        /// <summary>
        /// Does the specified task have no continuations?
        /// </summary>
        public static readonly Func<Task, bool> NoContinuations;
    }

}
