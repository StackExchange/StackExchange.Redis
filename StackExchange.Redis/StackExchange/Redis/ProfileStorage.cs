using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    class ProfileStorage : IProfiledCommand
    {
        #region IProfiledCommand Impl
        public EndPoint EndPoint
        {
            get { return Server.EndPoint; }
        }

        public int Db
        {
            get { return Message.Db; }
        }

        public string Command
        {
            get { return Message.Command.ToString(); }
        }

        public CommandFlags Flags
        {
            get { return Message.Flags; }
        }

        public DateTime CommandCreated
        {
            get { return MessageCreatedDateTime; }
        }

        public TimeSpan CreationToEnqueued
        {
            get { return TimeSpan.FromTicks(EnqueuedTimeStamp - MessageCreatedTimeStamp); }
        }

        public TimeSpan EnqueuedToSending
        {
            get { return TimeSpan.FromTicks(RequestSentTimeStamp - EnqueuedTimeStamp); }
        }

        public TimeSpan SentToResponse
        {
            get { return TimeSpan.FromTicks(ResponseReceivedTimeStamp - RequestSentTimeStamp); }
        }

        public TimeSpan ResponseToCompletion
        {
            get { return TimeSpan.FromTicks(CompletedTimeStamp - ResponseReceivedTimeStamp); }
        }

        public TimeSpan ElapsedTime
        {
            get { return TimeSpan.FromTicks(CompletedTimeStamp - MessageCreatedTimeStamp); }
        }

        public IProfiledCommand RetransmissionOf
        {
            get { return OriginalProfiling; }
        }

        public RetransmissionReasonType? RetransmissionReason
        {
            get { return Reason; }
        }
        #endregion

        public ProfileStorage NextElement { get; set; }

        private Message Message;
        private ServerEndPoint Server;
        private ProfileStorage OriginalProfiling;
        private RetransmissionReasonType? Reason;

        private DateTime MessageCreatedDateTime;
        private long MessageCreatedTimeStamp;
        private long EnqueuedTimeStamp;
        private long RequestSentTimeStamp;
        private long ResponseReceivedTimeStamp;
        private long CompletedTimeStamp;

        private ConcurrentProfileStorageCollection PushToWhenFinished;

        private ProfileStorage(ConcurrentProfileStorageCollection pushTo, ServerEndPoint server, ProfileStorage resentFor, RetransmissionReasonType? reason)
        {
            PushToWhenFinished = pushTo;
            OriginalProfiling = resentFor;
            Server = server;
            Reason = reason;
        }

        public static ProfileStorage NewWithContext(ConcurrentProfileStorageCollection pushTo, ServerEndPoint server)
        {
            return new ProfileStorage(pushTo, server, null, null);
        }

        public static ProfileStorage NewAttachedToSameContext(ProfileStorage resentFor, ServerEndPoint server, bool isMoved)
        {
            return new ProfileStorage(resentFor.PushToWhenFinished, server, resentFor, isMoved ? RetransmissionReasonType.Moved : RetransmissionReasonType.Ask);
        }

        public void SetMessage(Message msg)
        {
            // This method should never be called twice
            if (Message != null) throw new InvalidOperationException();

            Message = msg;
            MessageCreatedDateTime = msg.createdDateTime;
            MessageCreatedTimeStamp = msg.createdTimestamp;
        }

        public void SetEnqueued()
        {
            // This method should never be called twice
            if (EnqueuedTimeStamp > 0) throw new InvalidOperationException();

            EnqueuedTimeStamp = Stopwatch.GetTimestamp();
        }

        public void SetRequestSent()
        {
            // This method should never be called twice
            if (RequestSentTimeStamp > 0) throw new InvalidOperationException();

            RequestSentTimeStamp = Stopwatch.GetTimestamp();
        }

        public void SetResponseReceived()
        {
            if (ResponseReceivedTimeStamp > 0) throw new InvalidOperationException();

            ResponseReceivedTimeStamp = Stopwatch.GetTimestamp();
        }

        public void SetCompleted()
        {
            // this method can be called multiple times, depending on how the task completed (async vs not)
            //   so we actually have to guard against it.

            var now = Stopwatch.GetTimestamp();
            var oldVal = Interlocked.CompareExchange(ref CompletedTimeStamp, now, 0);

            // second call
            if (oldVal != 0) return;

            // only push on the first call, no dupes!
            PushToWhenFinished.Add(this);
        }

        public override string ToString()
        {
            return
                string.Format(
@"EndPoint = {0}
Db = {1}
Command = {2}
CommandCreated = {3:u}
CreationToEnqueued = {4}
EnqueuedToSending = {5}
SentToResponse = {6}
ResponseToCompletion = {7}
ElapsedTime = {8}
Flags = {9}
RetransmissionOf = ({10})",
                  EndPoint,
                  Db,
                  Command,
                  CommandCreated,
                  CreationToEnqueued,
                  EnqueuedToSending,
                  SentToResponse,
                  ResponseToCompletion,
                  ElapsedTime,
                  Flags,
                  RetransmissionOf
                );
        }
    }
}
