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
        GenericPolicy policy;
        /// <summary>
        /// 

        public PolicyRetry()
        {
            policy = new GenericPolicy();
        }
        /// <summary>
        /// 
        /// </summary>
        public IRetryPolicy AlwaysRetry() =>  policy.Set(failedCommand => true, handler);

        /// <summary>
        /// 
        /// </summary>
        public IRetryPolicy RetryIfNotYetSent() => policy.Set(failedCommand => failedCommand.Status == CommandStatus.WaitingToBeSent, handler);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRetry"></param>
        /// <returns></returns>
        public IRetryPolicy AlwaysRetry(Action<FailedCommand> onRetry)
            => policy.Set(failedCommand => { onRetry(failedCommand); return true; }, handler);

        /// <summary>
        /// 
        /// </summary>
        public IRetryPolicy RetryIfNotYetSent(Action<FailedCommand> onRetry)
            => policy.Set(failedCommand =>
            {
                onRetry(failedCommand);
                return failedCommand.Status == CommandStatus.WaitingToBeSent;
            }, handler);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="handleResult"></param>
        /// <returns></returns>
        public IRetryBuilder HandleResult(Func<FailedCommand, bool> handleResult)
        {
            policy.Set(handleResult);
            return this;
        }
        
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="handleResult"></param>
        /// <returns></returns>
        public IRetryBuilder HandleResult(Func<FailedCommand, bool> handleResult);
    }
}

