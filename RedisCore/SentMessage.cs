namespace RedisCore
{
    internal struct SentMessage
    {
        private ResultParser parser;
        private object source;

        public SentMessage(object source, ResultParser parser)
        {
            this.source = source;
            this.parser = parser;
        }

        internal void OnReceived(ref RawResult response) => parser?.Process(ref response, source);
    }
}
