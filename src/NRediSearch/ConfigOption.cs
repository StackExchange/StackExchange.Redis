using StackExchange.Redis;

namespace NRediSearch
{
    public sealed class ConfigOption
    {
        public static readonly ConfigOption NOGC = new ConfigOption(nameof(NOGC));
        public static readonly ConfigOption MINPREFIX = new ConfigOption(nameof(MINPREFIX));
        public static readonly ConfigOption MAXEXPANSIONS = new ConfigOption(nameof(MAXEXPANSIONS));
        public static readonly ConfigOption TIMEOUT = new ConfigOption(nameof(TIMEOUT));
        public static readonly ConfigOption ON_TIMEOUT = new ConfigOption(nameof(ON_TIMEOUT));
        public static readonly ConfigOption MIN_PHONETIC_TERM_LEN = new ConfigOption(nameof(MIN_PHONETIC_TERM_LEN));
        public static readonly ConfigOption ALL = new ConfigOption("*");

        public object Value { get; }

        private ConfigOption(string value)
        {
            Value = value.Literal();
        }

        internal static ConfigOption FromRedisResult(RedisResult value)
        {
            switch((string)value)
            {
                case nameof(NOGC):
                    return NOGC;
                case nameof(MINPREFIX):
                    return MINPREFIX;
                case nameof(MAXEXPANSIONS):
                    return MAXEXPANSIONS;
                case nameof(TIMEOUT):
                    return TIMEOUT;
                case nameof(ON_TIMEOUT):
                    return ON_TIMEOUT;
                case nameof(MIN_PHONETIC_TERM_LEN):
                    return MIN_PHONETIC_TERM_LEN;
                default:
                    return null;
            }
        }
    }
}
