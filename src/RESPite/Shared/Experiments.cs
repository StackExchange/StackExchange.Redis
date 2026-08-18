namespace RESPite
{
    // example usage:
    // [Experimental(Experiments.SomeFeature, UrlFormat = Experiments.UrlFormat)]
    // where SomeFeature has the next label, for example "SER042", and /docs/exp/SER042.md exists
    internal static class Experiments
    {
        // note: {0} is substituted with the DiagnosticId by the analyzer, e.g. .../exp/SER002
        public const string UrlFormat = "https://seredis.dev/exp/{0}";

        // Retired experiments: these server features are now stable and no longer gated.
        // The DiagnosticIds remain reserved (do NOT reuse them) so old callers' suppressions
        // and the /docs/exp pages stay meaningful:
        //   SER002 = Server_8_4  (Redis 8.4 features)
        //   SER003 = Server_8_6  (Redis 8.6 features)
        //   SER006 = Server_8_8  (Redis 8.8 features)

        // ReSharper disable InconsistentNaming
        public const string Respite = "SER004";
        public const string UnitTesting = "SER005";
        public const string GeoRedundantFailover = "SER007";
        public const string Server_8_10 = "SER008";
        public const string Transport = "SER009";

        // ReSharper restore InconsistentNaming

        // this one is not a real experiment; it exists to help me
        // spot bad API uses, via a DEBUG symbol
        public const string StringToRedisValue = "StringToRedisValue";
    }
}

#if !NET8_0_OR_GREATER
#pragma warning disable SA1403
namespace System.Diagnostics.CodeAnalysis
#pragma warning restore SA1403
{
    [AttributeUsage(
        AttributeTargets.Assembly |
        AttributeTargets.Module |
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Enum |
        AttributeTargets.Constructor |
        AttributeTargets.Method |
        AttributeTargets.Property |
        AttributeTargets.Field |
        AttributeTargets.Event |
        AttributeTargets.Interface |
        AttributeTargets.Delegate,
        Inherited = false)]
    internal sealed class ExperimentalAttribute(string diagnosticId) : Attribute
    {
        public string DiagnosticId { get; } = diagnosticId;
        public string? UrlFormat { get; set; }
        public string? Message { get; set; }
    }
}
#endif
