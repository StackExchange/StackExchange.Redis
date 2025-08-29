using System;
using System.Diagnostics;

namespace Resp;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
[Conditional("DEBUG")]
public sealed class RespCommandAttribute(string? command = null) : Attribute
{
    public string? Command => command;
    public string? Formatter { get; set; }
    public string? Parser { get; set; }

    public static class Parsers
    {
        private const string Prefix = "global::Resp.RespParsers.";

        /// <inheritdoc cref="ResponseSummary.Parser"/>
        public const string Summary = "global::Resp." + nameof(ResponseSummary) + "." + nameof(ResponseSummary.Parser);

        public const string ByteArray = Prefix + nameof(RespParsers.ByteArray);
        public const string String = Prefix + nameof(RespParsers.String);
        public const string Int32 = Prefix + nameof(RespParsers.Int32);
        public const string Int64 = Prefix + nameof(RespParsers.Int64);
        public const string NullableInt64 = Prefix + nameof(RespParsers.NullableInt64);
        public const string NullableInt32 = Prefix + nameof(RespParsers.NullableInt32);
        public const string NullableSingle = Prefix + nameof(RespParsers.NullableSingle);
        public const string BufferWriter = Prefix + nameof(RespParsers.BufferWriter);
        public const string ByteArrayArray = Prefix + nameof(RespParsers.ByteArrayArray);
        public const string OK = Prefix + nameof(RespParsers.OK);
        public const string Single = Prefix + nameof(RespParsers.Single);
        public const string Double = Prefix + nameof(RespParsers.Double);
        public const string Success = Prefix + nameof(RespParsers.Success);
        public const string NullableDouble = Prefix + nameof(RespParsers.NullableDouble);
    }
}
