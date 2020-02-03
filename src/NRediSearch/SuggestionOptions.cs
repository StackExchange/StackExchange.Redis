// .NET port of https://github.com/RedisLabs/JRediSearch/
using System;

namespace NRediSearch
{
    public class SuggestionOptions
    {
        private readonly object WITHPAYLOADS_FLAG = "WITHPAYLOADS".Literal();
        private readonly object WITHSCORES_FLAG = "WITHSCORES".Literal();

        public SuggestionOptions(SuggestionOptionsBuilder builder)
        {
            With = builder._with;
            Fuzzy = builder._fuzzy;
            Max = builder._max;
        }

        public static SuggestionOptionsBuilder Builder => new SuggestionOptionsBuilder();

        public WithOptions With { get; }

        public bool Fuzzy { get; }

        public int Max { get; } = 5;

        public object[] GetFlags()
        {
            if (HasOption(WithOptions.PayloadsAndScores))
            {
                return new[] { WITHPAYLOADS_FLAG, WITHSCORES_FLAG };
            }

            if (HasOption(WithOptions.Payloads))
            {
                return new[] { WITHPAYLOADS_FLAG };
            }

            if (HasOption(WithOptions.Scores))
            {
                return new[] { WITHSCORES_FLAG };
            }

            return default;
        }

        public SuggestionOptionsBuilder ToBuilder() => new SuggestionOptionsBuilder(this);

        internal bool GetIsPayloadAndScores() => HasOption(WithOptions.PayloadsAndScores);

        internal bool GetIsPayload() => HasOption(WithOptions.Payloads);

        internal bool GetIsScores() => HasOption(WithOptions.Scores);

        [Flags]
        public enum WithOptions
        {
            None = 0,
            Payloads = 1,
            Scores = 2,
            PayloadsAndScores = Payloads | Scores
        }

        internal bool HasOption(WithOptions option) => (With & option) != 0;

        public sealed class SuggestionOptionsBuilder
        {
            internal WithOptions _with;
            internal bool _fuzzy;
            internal int _max = 5;

            public SuggestionOptionsBuilder() { }

            public SuggestionOptionsBuilder(SuggestionOptions options)
            {
                _with = options.With;
                _fuzzy = options.Fuzzy;
                _max = options.Max;
            }

            public SuggestionOptionsBuilder Fuzzy()
            {
                _fuzzy = true;

                return this;
            }

            public SuggestionOptionsBuilder Max(int max)
            {
                _max = max;

                return this;
            }

            public SuggestionOptionsBuilder With(WithOptions with)
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
