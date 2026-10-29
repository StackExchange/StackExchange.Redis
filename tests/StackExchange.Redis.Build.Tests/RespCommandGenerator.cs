using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// <c>[Resp]</c> on a <c>RespCommand</c>: what the generator emits, and what it refuses (SER351).
/// </summary>
/// <remarks>
/// Run through a generator driver over a real compilation, because the interesting part is what is emitted - the
/// analyzer harness only checks diagnostics. The rendering behaviour of the result is tested where it is used, in
/// <c>RespCommandTests</c>.
/// </remarks>
public class RespCommandGenerator
{
    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Protocol.RespCommand).Assembly.Location))
            .ToArray();

        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source, parse)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new RespFragmentGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    private const string Usings = "using StackExchange.Redis.Protocol;\n";

    [Fact]
    public void AModuleCommandBecomesAFieldResolvedOnce()
    {
        var (result, output) = Run(Usings + """
            public static partial class Cms
            {
                [Resp("CMS.INFO")]
                public static partial RespCommand Info { get; }
            }
            """);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("private static readonly global::StackExchange.Redis.Protocol.RespCommand __RespCommand_Info = global::StackExchange.Redis.Protocol.RespCommands.Command(\"CMS.INFO\"u8);", generated);
        Assert.Contains("public static partial global::StackExchange.Redis.Protocol.RespCommand Info => __RespCommand_Info;", generated);

        // and the whole thing compiles: the declaration and the generated part fit together
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ANameIsInferredFromTheMember()
    {
        var (result, _) = Run(Usings + """
            public static partial class Commands
            {
                [Resp]
                internal static partial RespCommand Get { get; }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Command(\"GET\"u8)", Assert.Single(result.GeneratedTrees).ToString());
    }

    [Theory]
    [InlineData("[Resp(\"CLIENT\", \"KILL\")]", "single command name")]
    [InlineData("[Resp(\"CMS INFO\")]", "printable ASCII without whitespace")]
    [InlineData("[Resp(\"CMS.INF\\u00d8\")]", "printable ASCII without whitespace")]
    public void ABadCommandIsRefusedAtBuildTime(string attribute, string because)
    {
        var (result, _) = Run(Usings + "public static partial class C { " + attribute + " public static partial RespCommand X { get; } }");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SER351", diagnostic.Id);
        Assert.Contains(because, diagnostic.GetMessage());
    }

    [Fact]
    public void AnotherTypeIsStillRefused()
    {
        var (result, _) = Run(Usings + "public static partial class C { [Resp] public static partial int X { get; } }");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SER351", diagnostic.Id);
        Assert.Contains("not RespFragment or RespCommand", diagnostic.GetMessage());
    }
}
