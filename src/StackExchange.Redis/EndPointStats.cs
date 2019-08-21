namespace StackExchange.Redis
{
    /// <summary>
    /// provides stats information of an endpoint in the connection multiplexer
    /// </summary>
    public readonly struct EndPointStats
    {
        /// <summary>
        /// number of requests waiting on the client side to be written to the socket
        /// </summary>
        public int QueuedAwaitingWrite { get; }

        /// <summary>
        /// number of requests waiting on the client side for a response
        /// </summary>
        public int QueuedAwaitingResponse { get;}

        /// <summary>
        /// Create Endpointstats with the stats
        /// </summary>
        /// <param name="queuedAwaitingWrite"></param>
        /// <param name="queuedAwaitingResponse"></param>
        public EndPointStats(int queuedAwaitingWrite, int queuedAwaitingResponse)
        {
            QueuedAwaitingWrite = queuedAwaitingWrite;
            QueuedAwaitingResponse = queuedAwaitingResponse;
        }


    }
}
