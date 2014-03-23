using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// We want to prevent callers hijacking the reader thread; this is a bit nasty, but works;
    /// see http://stackoverflow.com/a/22588431/23354 for more information; a huge
    /// thanks to Eli Arbel for spotting this (even though it is pure evil; it is *my kind of evil*)
    /// </summary>
#if DEBUG
    public // for the unit tests in TaskTests.cs
#endif
    static class TaskSource
    {
        /// <summary>
        /// Indicates whether the specified task will not hijack threads when results are set
        /// </summary>
        public static readonly Func<Task, bool> IsSyncSafe;
        static Action<Task> denyExecSync;
        static TaskSource()
        {
            try
            {
                var stateField = typeof(Task).GetField("m_stateFlags", BindingFlags.Instance | BindingFlags.NonPublic);
                if (stateField != null)
                {
                    var constField = typeof(Task).GetField("TASK_STATE_THREAD_WAS_ABORTED", BindingFlags.Static | BindingFlags.NonPublic);
                    // try to use the literal field value, but settle for hard-coded if it isn't there
                    int flag = constField == null ? 134217728 : (int)constField.GetRawConstantValue();

                    var method = new DynamicMethod("DenyExecSync", null, new[] { typeof(Task) }, typeof(Task), true);
                    var il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0); // [task]
                    il.Emit(OpCodes.Ldarg_0); // [task, task]
                    il.Emit(OpCodes.Ldfld, stateField); // [task, flags]
                    il.Emit(OpCodes.Ldc_I4, flag); // [task, flags, 134217728]
                    il.Emit(OpCodes.Or); // [task, combined]
                    il.Emit(OpCodes.Stfld, stateField); // []
                    il.Emit(OpCodes.Ret);
                    denyExecSync = (Action<Task>)method.CreateDelegate(typeof(Action<Task>));

                    method = new DynamicMethod("IsSyncSafe", typeof(bool), new[] { typeof(Task) }, typeof(Task), true);
                    il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldc_I4, flag); // [134217728]
                    il.Emit(OpCodes.Ldarg_0); // [134217728, task]
                    il.Emit(OpCodes.Ldfld, stateField); // [134217728, flags]
                    il.Emit(OpCodes.Ldc_I4, flag); // [134217728, flags, 134217728]
                    il.Emit(OpCodes.And); // [134217728, single-flag]
                    il.Emit(OpCodes.Ceq); // [true/false]
                    il.Emit(OpCodes.Ret);
                    IsSyncSafe = (Func<Task, bool>)method.CreateDelegate(typeof(Func<Task, bool>));

                    // and test them (check for an exception etc)
                    var tcs = new TaskCompletionSource<int>();
                    denyExecSync(tcs.Task);
                    if(!IsSyncSafe(tcs.Task))
                    {
                        Debug.WriteLine("IsSyncSafe reported false!");
                        Trace.WriteLine("IsSyncSafe reported false!");
                        // revert to not trusting them
                        denyExecSync = null;
                        IsSyncSafe = null;
                    }
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Trace.WriteLine(ex.Message);
            }

            if(denyExecSync == null)
                denyExecSync = t => { }; // no-op if that fails
            if (IsSyncSafe == null)
                IsSyncSafe = t => false; // assume: not
        }

        /// <summary>
        /// Create a new TaskCompletionSource that will not allow result-setting threads to be hijacked
        /// </summary>
        public static TaskCompletionSource<T> CreateDenyExecSync<T>(object asyncState)
        {
            var source = new TaskCompletionSource<T>(asyncState);
            denyExecSync(source.Task);
            return source;
        }
        /// <summary>
        /// Create a new TaskCompletion source
        /// </summary>
        public static TaskCompletionSource<T> Create<T>(object asyncState)
        {
            return new TaskCompletionSource<T>(asyncState);
        }
    }
}
