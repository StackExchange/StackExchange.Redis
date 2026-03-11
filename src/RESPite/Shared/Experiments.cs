namespace RESPite
{
    // example usage:
    // [Experimental(Experiments.SomeFeature, UrlFormat = Experiments.UrlFormat)]
    // where SomeFeature has the next label, for example "SER042", and /docs/exp/SER042.md exists
    internal static class Experiments
    {
        public const string UrlFormat = "https://stackexchange.github.io/StackExchange.Redis/exp/";

        // ReSharper disable InconsistentNaming
        public const string VectorSets = "SER001";
        public const string Server_8_4 = "SER002";
        public const string Server_8_6 = "SER003";
        public const string Respite = "SER004";
        public const string UnitTesting = "SER005";
        // ReSharper restore InconsistentNaming
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
