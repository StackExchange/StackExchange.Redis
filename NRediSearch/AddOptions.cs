// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch
{
    public sealed class AddOptions
    {
        public enum ReplacementPolicy
        {
            /// <summary>
            /// The default mode. This will cause the add operation to fail if the document already exists
            /// </summary>
            None,
            /// <summary>
            /// Replace/reindex the entire document. This has the effect of atomically deleting the previous
            /// document and replacing it with the context of the new document. Fields in the old document which
            /// are not present in the new document are lost
            /// </summary>
            Full,
            /// <summary>
            /// Only reindex/replace fields that are updated in the command. Fields in the old document which are
            /// not present in the new document are preserved.Fields that are present in both are overwritten by
            /// the new document
            /// </summary>
            Partial,
        }

        public string Language { get; set; }
        public bool NoSave { get; set; }
        public ReplacementPolicy ReplacePolicy { get; set; }

        /// <summary>
        /// Create a new DocumentOptions object. Methods can later be chained via a builder-like pattern
        /// </summary>
        public AddOptions() { }

        /// <summary>
        /// Set the indexing language
        /// </summary>
        /// <param name="language">Set the indexing language</param>
        public AddOptions SetLanguage(string language)
        {
            Language = language;
            return this;
        }
        /// <summary>
        ///  Whether document's contents should not be stored in the database.
        /// </summary>
        /// <param name="enabled">if enabled, the document is <b>not</b> stored on the server. This saves disk/memory space on the
        /// server but prevents retrieving the document itself.</param>
        public AddOptions SetNoSave(bool enabled)
        {
            NoSave = enabled;
            return this;
        }

        /// <summary>
        /// Indicate the behavior for the existing document.
        /// </summary>
        /// <param name="mode">One of the replacement modes.</param>
        public AddOptions SetReplacementPolicy(ReplacementPolicy mode)
        {
            ReplacePolicy = mode;
            return this;
        }
    }
}
