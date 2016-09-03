namespace RedisCore
{
    internal struct SentMessage
    {
        private ResultParser parser;
        private object source;
        private bool getResult;

        public SentMessage(object source, ResultParser parser, bool getResult)
        {
            this.source = source;
            this.parser = parser;
            this.getResult = getResult;
        }

        internal void OnReceived(ref RawResult response) => parser?.Process(ref response, source, getResult);
    }
}
