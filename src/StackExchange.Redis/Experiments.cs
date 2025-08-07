namespace StackExchange.Redis
{
    internal static class Experiments
    {
        public const string UrlFormat = "https://stackexchange.github.io/StackExchange.Redis/exp/{0}";
        public const string DelayTunnel = "SER001";
    }
}

#if !NET8_0_OR_GREATER
#pragma warning disable SA1403
namespace System.Diagnostics.CodeAnalysis
#pragma warning restore SA1403
{
    /// <summary>Indicates that an API is experimental and it may change in the future.</summary>
    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Enum | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property |
        AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate,
        Inherited = false)]
    internal sealed class ExperimentalAttribute : Attribute
    {
        /// <summary>Initializes a new instance of the <see cref="T:System.Diagnostics.CodeAnalysis.ExperimentalAttribute" /> class, specifying the ID that the compiler will use when reporting a use of the API the attribute applies to.</summary>
        /// <param name="diagnosticId">The ID that the compiler will use when reporting a use of the API the attribute applies to.</param>
        public ExperimentalAttribute(string diagnosticId) => this.DiagnosticId = diagnosticId;

        /// <summary>Gets the ID that the compiler will use when reporting a use of the API the attribute applies to.</summary>
        /// <returns>The unique diagnostic ID.</returns>
        public string DiagnosticId { get; }

        /// <summary>Gets or sets the URL for corresponding documentation.
        /// The API accepts a format string instead of an actual URL, creating a generic URL that includes the diagnostic ID.</summary>
        /// <returns>The format string that represents a URL to corresponding documentation.</returns>
        public string? UrlFormat { get; set; }
    }
}
#endif
