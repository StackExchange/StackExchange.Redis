namespace RESPite
{
    // example usage:
    // [Experimental(Experiments.SomeFeature, UrlFormat = Experiments.UrlFormat)]
    // where SomeFeature has the next label, for example "SER042", and /docs/exp/SER042.md exists
    internal static class Experiments
    {
        // note: {0} is substituted with the DiagnosticId by the analyzer, e.g. .../exp/SER002
        public const string UrlFormat = "https://stackexchange.github.io/StackExchange.Redis/exp/{0}";

        // ReSharper disable InconsistentNaming
        public const string Server_8_4 = "SER002";
        public const string Server_8_6 = "SER003";
        public const string Respite = "SER004";
        public const string UnitTesting = "SER005";
        public const string Server_8_8 = "SER006";

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
