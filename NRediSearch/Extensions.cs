using static NRediSearch.Client;

namespace NRediSearch
{
    public static class Extensions
    {
        /// <summary>
        /// Set a custom stopword list
        /// </summary>
        public static ConfiguredIndexOptions SetStopwords(this IndexOptions options, params string[] stopwords)
            => new ConfiguredIndexOptions(options).SetStopwords(stopwords);
    }
}
