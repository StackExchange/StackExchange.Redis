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
    public static class RetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action"></param>
        /// <returns></returns>
        public static IRetryBuilder Handle<T>(Func <Exception, bool> action) where T:RedisException
        => new PolicyRetry(exception =>
        {
            return exception is T;
        });

      

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static IRetryBuilder HandleConnectionException()
        {
            return Handle<RedisConnectionException>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IRetryBuilder Handle<T>() where T:RedisException
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
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action"></param>
        /// <returns></returns>
        public static IRetryBuilder HandleFailedCommand<T>(Func<FailedCommand, bool> action) where T : RedisException
       => new PolicyRetry(exception =>
       {
           return exception is T;
       });
        /// <summary>
        /// 
        /// </summary>
        /// <param name="handler"></param>
        public PolicyRetry(Func<Exception, bool> handler)
        {
            this.handler = handler;
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
        /// <param name="beforeRetry"></param>
        /// <returns></returns>
        public IRetryPolicy AlwaysRetry(Action<FailedCommand, int> beforeRetry) => throw new NotImplementedException();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRetry"></param>
        /// <returns></returns>
        public IRetryPolicy Retry(Action<FailedCommand> onRetry) => throw new NotImplementedException();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public IRetryBuilder HandleFailedCommand(Func<FailedCommand, bool> action) => throw new NotImplementedException();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public IRetryBuilder HandleFailedCommand(Func<FailedCommand, int, bool> action) => throw new NotImplementedException();
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
      /// <param name="beforeRetry"></param>
      /// <returns></returns>
        public IRetryPolicy AlwaysRetry(Action<FailedCommand, int> beforeRetry);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRetry"></param>
        /// <returns></returns>
        public IRetryPolicy Retry(Action<FailedCommand> onRetry);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public IRetryBuilder HandleFailedCommand(Func<FailedCommand, bool> action);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public IRetryBuilder HandleFailedCommand(Func<FailedCommand, int, bool> action);

    }
}

