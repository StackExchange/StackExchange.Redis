using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// 
    /// </summary>
    public static class Policy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action"></param>
        /// <returns></returns>
        public static IRetryBuilder Handle<T>(Func<FailedCommand, bool> action)
        => new PolicyRetry(exception =>
        {
            return exception is T;
        });

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IRetryBuilder Handle<T>()
       => new PolicyRetry(exception =>
       {
           return exception is T;
       });
    }

    /// <summary>
    /// 
    /// </summary>
    public class PolicyRetry : IRetryBuilder
    {
        Func<Exception, bool> handler;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="func"></param>
        public PolicyRetry(Func<Exception, bool> func)
        {
            handler = func;
        }
        /// <summary>
        /// 
        /// </summary>
        public IRetryPolicy AlwaysRetry() => new GenericPolicy(failedCommand => true, handler);

        /// <summary>
        /// 
        /// </summary>
        public IRetryPolicy RetryIfNotYetSent() => new GenericPolicy(failedCommand => failedCommand.Status == CommandStatus.WaitingToBeSent, handler);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRetry"></param>
        /// <returns></returns>
        public IRetryPolicy AlwaysRetry(Action<FailedCommand> onRetry)
            => new GenericPolicy(failedCommand => { onRetry(failedCommand); return true; }, handler);

        /// <summary>
        /// 
        /// </summary>
        public IRetryPolicy RetryIfNotYetSent(Action<FailedCommand> onRetry)
            => new GenericPolicy(failedCommand =>
            {
                onRetry(failedCommand);
                return failedCommand.Status == CommandStatus.WaitingToBeSent;
            }, handler);
    }
    /// <summary>
    /// 
    /// </summary>
    public interface IRetryBuilder
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IRetryPolicy AlwaysRetry();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IRetryPolicy RetryIfNotYetSent();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRetry"></param>
        /// <returns></returns>
        public IRetryPolicy AlwaysRetry(Action<FailedCommand> onRetry);
    }
}

