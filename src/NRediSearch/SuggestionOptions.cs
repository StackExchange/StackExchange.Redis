// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch
{
    public class SuggestionOptions
    {
        private const string WITHPAYLOADS_FLAG = "WITHPAYLOADS";
        private const string WITHSCORES_FLAG = "WITHSCORES";

        public SuggestionOptions(Builder builder)
        {
            _with = builder._with;
            Fuzzy = builder._fuzzy;
            Max = builder._max;
        }

        public static Builder GetBuilder() => new Builder();

        private With _with;
        public With GetWith() => _with;

        public bool Fuzzy { get; }

        public int Max { get; } = 5;

        public Builder ToBuilder() => new Builder(this);

        public class With
        {
            internal bool IsPayload { get; } = false;
            public static With PAYLOAD = new With(true, false, false, WITHPAYLOADS_FLAG.Literal());

            internal bool IsScores { get; } = false;
            public static With SCORES = new With(false, true, false, WITHSCORES_FLAG.Literal());

            internal bool IsPayloadAndScores { get; } = false;
            public static With PAYLOAD_AND_SCORES = new With(false, false, true, WITHPAYLOADS_FLAG.Literal(), WITHSCORES_FLAG.Literal());

            public object[] Flags { get; }

            private With(bool isPayload, bool isScores, bool isPayloadAndScores, params object[] flags)
            {
                Flags = flags;
                IsPayload = isPayload;
                IsScores = isScores;
                IsPayloadAndScores = isPayloadAndScores;
            }
        }

        public sealed class Builder
        {
            internal With _with;
            internal bool _fuzzy;
            internal int _max = 5;

            public Builder() { }

            public Builder(SuggestionOptions options)
            {
                _with = options.GetWith();
                _fuzzy = options.Fuzzy;
                _max = options.Max;
            }

            public Builder Fuzzy()
            {
                _fuzzy = true;

                return this;
            }

            public Builder Max(int max)
            {
                _max = max;

                return this;
            }

            public Builder With(SuggestionOptions.With with)
            {
                _with = with;

                return this;
            }

            public SuggestionOptions Build()
            {
                return new SuggestionOptions(this);
            }
        }
    }
}
