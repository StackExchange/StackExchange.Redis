// .NET port of https://github.com/RedisLabs/JRediSearch/
using System;
using StackExchange.Redis;

namespace NRediSearch
{
    public sealed class Suggestion
    {
        public string String { get; }
        public double Score { get; }
        public string Payload { get; }

        private Suggestion(SuggestionBuilder builder)
        {
            String = builder._string;
            Score = builder._score;
            Payload = builder._payload;
        }

        public override bool Equals(object obj)
        {
            if (this == obj)
            {
                return true;
            }

            if(!(obj is Suggestion that))
            {
                return false;
            }

            return Score == that.Score && String == that.String && Payload == that.Payload;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;

                hash = hash * 31 + String.GetHashCode();
                hash = hash * 31 + Score.GetHashCode();
                hash = hash * 31 + Payload.GetHashCode();

                return hash;
            }
        }

        public override string ToString() =>
            $"Suggestion{{string='{String}', score={Score}, payload='{Payload}'}}";

        public SuggestionBuilder ToBuilder() => new SuggestionBuilder(this);

        public static SuggestionBuilder Builder => new SuggestionBuilder();

        public sealed class SuggestionBuilder
        {
            internal string _string;
            internal double _score = 1.0;
            internal string _payload;

            public SuggestionBuilder() { }

            public SuggestionBuilder(Suggestion suggestion)
            {
                _string = suggestion.String;
                _score = suggestion.Score;
                _payload = suggestion.Payload;
            }

            public SuggestionBuilder String(string @string)
            {
                _string = @string;

                return this;
            }

            public SuggestionBuilder Score(double score)
            {
                _score = score;

                return this;
            }

            public SuggestionBuilder Payload(string payload)
            {
                _payload = payload;

                return this;
            }

            public Suggestion Build() => Build(false);

            internal Suggestion Build(bool fromServer)
            {
                bool isStringMissing = _string == null;
                bool isScoreOutOfRange = !fromServer && (_score < 0.0 || _score > 1.0);

                if (isStringMissing || isScoreOutOfRange)
                {
                    throw new RedisCommandException($"Missing required fields: {(isStringMissing ? "string" : string.Empty)} {(isScoreOutOfRange ? "score not within range" : string.Empty)}: {_score.ToString()}");
                }

                return new Suggestion(this);
            }
        }
    }
}
