// .NET port of https://github.com/RedisLabs/JRediSearch/
using System;
using StackExchange.Redis;

namespace NRediSearch
{
    public class Suggestion
    {
        public string String { get; }
        public double Score { get; }
        public string Payload { get; }

        private Suggestion(Builder builder)
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

            if (obj == null || obj.GetType() != GetType())
            {
                return false;
            }

            var that = obj as Suggestion;

            return Score.Equals(that.Score) && String.Equals(that.String) && Payload.Equals(that.Payload);
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

        public Builder ToBuilder() => new Builder(this);

        public static Builder GetBuilder() => new Builder();

        public sealed class Builder
        {
            internal string _string;
            internal double _score = 1.0;
            internal string _payload;

            public Builder() { }

            public Builder(Suggestion suggestion)
            {
                _string = suggestion.String;
                _score = suggestion.Score;
                _payload = suggestion.Payload;
            }

            public Builder String(string @string)
            {
                _string = @string;

                return this;
            }

            public Builder Score(double score)
            {
                _score = score;

                return this;
            }

            public Builder Payload(string payload)
            {
                _payload = payload;

                return this;
            }

            public Suggestion Build()
            {
                bool isStringMissing = _string == null;
                bool isScoreOutOfRange = (_score < 0.0 || _score > 1.0);

                if (isStringMissing || isScoreOutOfRange)
                {
                    throw new RedisCommandException($"Missing required fields: {(isStringMissing ? "string" : string.Empty)} {(isScoreOutOfRange ? "score not within range" : string.Empty)}");
                }

                return new Suggestion(this);
            }
        }
    }
}
