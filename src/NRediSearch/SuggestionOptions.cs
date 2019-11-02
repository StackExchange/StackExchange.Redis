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

        public int Max { get; }

        public Builder ToBuilder() => new Builder(this);

        public class With
        {
            public static With PAYLOAD = new With(WITHPAYLOADS_FLAG);
            public static With SCORES = new With(WITHSCORES_FLAG);
            public static With PAYLOAD_AND_SCORES = new With(WITHPAYLOADS_FLAG, WITHSCORES_FLAG);

            public string[] Flags { get; }

            private With(params string[] flags) => Flags = flags;
        }

        public sealed class Builder
        {
            internal With _with;
            internal bool _fuzzy;
            internal int _max;

            public Builder() { }

            public Builder(SuggestionOptions options)
            {
                _with = options.GetWith();
                _fuzzy = options.Fuzzy;
                _max = options.Max;
            }

            public Builder Fuzzy()
            {
                return this;
            }

            public Builder Max(int max)
            {
                return this;
            }

            public SuggestionOptions Build()
            {
                return new SuggestionOptions(this);
            }
        }
    }
}
