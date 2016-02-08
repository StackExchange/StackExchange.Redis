using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace StackExchange.Redis
{
    class ProfileStorage : IProfiledCommand
    {
        #region IProfiledCommand Impl
        public EndPoint EndPoint => Server.EndPoint;

        public int Db => Message.Db;

        public string Command => Message.Command.ToString();

        public CommandFlags Flags => Message.Flags;

        public DateTime CommandCreated => MessageCreatedDateTime;

        public TimeSpan CreationToEnqueued => TimeSpan.FromTicks(EnqueuedTimeStamp - MessageCreatedTimeStamp);

        public TimeSpan EnqueuedToSending => TimeSpan.FromTicks(RequestSentTimeStamp - EnqueuedTimeStamp);

        public TimeSpan SentToResponse => TimeSpan.FromTicks(ResponseReceivedTimeStamp - RequestSentTimeStamp);

        public TimeSpan ResponseToCompletion => TimeSpan.FromTicks(CompletedTimeStamp - ResponseReceivedTimeStamp);

        public TimeSpan ElapsedTime => TimeSpan.FromTicks(CompletedTimeStamp - MessageCreatedTimeStamp);

        public IProfiledCommand RetransmissionOf => OriginalProfiling;

        public RetransmissionReasonType? RetransmissionReason { get; }

        #endregion

        public ProfileStorage NextElement { get; set; }

        private Message Message;
        private ServerEndPoint Server;
        private ProfileStorage OriginalProfiling;

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
            RetransmissionReason = reason;
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
                $@"EndPoint = {EndPoint}
Db = {Db}
Command = {Command}
CommandCreated = {CommandCreated:u}
CreationToEnqueued = {CreationToEnqueued}
EnqueuedToSending = {EnqueuedToSending}
SentToResponse = {SentToResponse}
ResponseToCompletion = {ResponseToCompletion}
ElapsedTime = {ElapsedTime}
Flags = {Flags}
RetransmissionOf = ({RetransmissionOf})";
        }
    }
}
