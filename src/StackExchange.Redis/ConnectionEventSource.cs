using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;

namespace StackExchange.Redis
{
    internal sealed class ConnectionEventSource : EventSource
    {
        public ConnectionEventSource() : base("StackExchange.Redis.Connection", EventSourceSettings.EtwSelfDescribingEventFormat, "StackExchange.Redis", "true")
        {
        }

        #region ConnectionMultiplexer
        [Event((int)EventId.ConnectionMultiplexer_ConnectionFailed, Message = Messages.ConnectionMultiplexer_ConnectionFailed, Level = EventLevel.Warning)]
        public void ConnectionMultiplexer_ConnectionFailed(string reconfigure, string connectionType, string failureType, string exceptionType, string exceptionMessage)
        {
            WriteEvent((int)EventId.ConnectionMultiplexer_ConnectionFailed, reconfigure, connectionType, failureType, exceptionType, exceptionMessage);
        }

        [Event((int)EventId.ConnectionMultiplexer_InternalError, Message = Messages.ConnectionMultiplexer_InternalError, Level = EventLevel.Warning)]
        public void ConnectionMultiplexer_InternalError(string connectionType, string callerName, string exceptionType, string exceptionMessage)
        {
            WriteEvent((int)EventId.ConnectionMultiplexer_InternalError, connectionType, callerName, exceptionType, exceptionMessage);
        }


        [Event((int)EventId.ConnectionMultiplexer_ConnectionRestored, Message = Messages.ConnectionMultiplexer_ConnectionRestored, Level = EventLevel.Informational)]
        public void ConnectionMultiplexer_ConnectionRestored(string connectionType, string reconfigured)
        {
            WriteEvent((int)EventId.ConnectionMultiplexer_ConnectionRestored, connectionType, reconfigured);
        }

        [Event((int)EventId.ConnectionMultiplexer_EndpointChanged, Message = Messages.ConnectionMultiplexer_EndpointChanged, Level = EventLevel.Informational)]
        public void ConnectionMultiplexer_EndpointChanged(string addressFamily)
        {
            WriteEvent((int)EventId.ConnectionMultiplexer_EndpointChanged, addressFamily);
        }

        #endregion ConnectionMultiplexer

        #region Exception
        [NonEvent]
        public void Exception(Exception ex)
        {
            if (this.IsEnabled())
            {
                Dictionary<string, string> data = new Dictionary<string, string>();
                var enumerator = ex.Data.GetEnumerator();                
                enumerator.Reset();
                while (enumerator.MoveNext())
                {
                    if(enumerator.Key is string && enumerator.Value is string)
                    {
                        data[enumerator.Key as string] = enumerator.Value as string;
                    }                    
                }
                Exception(ex.GetType().ToString(), ex.Message, ex.StackTrace, _serializeDictionary(data));
            }
        }

        [Event((int)EventId.Exception, Message = Messages.Exception, Level = EventLevel.Error)]
        public void Exception(string type, string message, string stackTrace, string data)
        {
            WriteEvent((int)EventId.Exception, type, message, stackTrace, data);
        }
        #endregion Exception

        public static ConnectionEventSource Log = new ConnectionEventSource();

        private string _serializeDictionary(Dictionary<string, string> dictionary)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            foreach (var kv in dictionary)
            {
                sb.Append($@"""{kv.Key}"":""{kv.Value}"",");
            }
            sb.Remove(sb.Length - 1, 1);
            sb.Append("}");
            return sb.ToString();
        }

        internal class Messages
        {
            public const string ConnectionMultiplexer_ConnectionFailed = "ConnectionMultiplexer connection failed. Reconfigure: {0}, ConnectionType: {1}, FailureType: {2}, {3}:{4}";
            public const string ConnectionMultiplexer_InternalError = "ConnectionMultiplexer internal error. ConnectionType: {0}, CallerName: {1}, {2}:{3}";
            public const string ConnectionMultiplexer_ConnectionRestored = "ConnectionMultiplexer connection restored. ConnectionType: {0}, Reconfigured: {1}";
            public const string ConnectionMultiplexer_EndpointChanged = "ConnectionMultiplexer endpoint changed. AddressFamily: {0}";
            public const string Exception = "Type: {0} \nMessage: {1} \nStackTrace: {2} \nData: {3}";
        }

        internal enum EventId
        {
            ConnectionMultiplexer_ConnectionFailed = 10,
            ConnectionMultiplexer_InternalError = 20,
            ConnectionMultiplexer_ConnectionRestored = 30,
            ConnectionMultiplexer_EndpointChanged= 40,
            Exception = 1000
        }
    }
}
