using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StackExchange.Redis.Build;

[Generator(LanguageNames.CSharp)]
public class RespCommandGenerator : IIncrementalGenerator
{
    [Flags]
    private enum LiteralFlags
    {
        None = 0,
        Suffix = 1 << 0, // else prefix
        // optional, etc
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var literals = context.SyntaxProvider
            .CreateSyntaxProvider(Predicate, Transform)
            .Where(pair => pair.MethodName is { Length: > 0 })
            .Collect();

        context.RegisterSourceOutput(literals, Generate);
    }

    private bool Predicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        // looking for [FastHash] partial static class Foo { }
        if (node is MethodDeclarationSyntax decl
            && decl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            foreach (var attribList in decl.AttributeLists)
            {
                foreach (var attrib in attribList.Attributes)
                {
                    if (attrib.Name.ToString() is "RespCommandAttribute" or "RespCommand") return true;
                }
            }
        }

        return false;
    }

    private readonly record struct LiteralTuple(string Token, LiteralFlags Flags);

    private readonly record struct ParameterTuple(
        string Type,
        string Name,
        string Modifiers,
        ParameterFlags Flags,
        EasyArray<LiteralTuple> Literals);

    private readonly record struct MethodTuple(
        string Namespace,
        string TypeName,
        string ReturnType,
        string MethodName,
        string Command,
        EasyArray<ParameterTuple> Parameters,
        string TypeModifiers,
        string MethodModifiers,
        string Context,
        string? Formatter,
        string? Parser,
        string DebugNotes);

    private static string GetFullName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private enum RESPite
    {
        RespContext,
        RespCommandAttribute,
        RespKeyAttribute,
        RespPrefixAttribute,
        RespSuffixAttribute,
    }

    private static bool IsRESPite(ITypeSymbol? symbol, RESPite type)
    {
        static string NameOf(RESPite type) => type switch
        {
            RESPite.RespContext => nameof(RESPite.RespContext),
            RESPite.RespCommandAttribute => nameof(RESPite.RespCommandAttribute),
            RESPite.RespKeyAttribute => nameof(RESPite.RespKeyAttribute),
            RESPite.RespPrefixAttribute => nameof(RESPite.RespPrefixAttribute),
            RESPite.RespSuffixAttribute => nameof(RESPite.RespSuffixAttribute),
            _ => type.ToString(),
        };

        if (symbol is INamedTypeSymbol named && named.Name == NameOf(type))
        {
            // looking likely; check namespace
            if (named.ContainingNamespace is { Name: "RESPite", ContainingNamespace.IsGlobalNamespace: true })
            {
                return true;
            }

            // if the type doesn't resolve: we're going to need to trust it
            if (named.TypeKind == TypeKind.Error) return true;
        }

        return false;
    }

    private enum SERedis
    {
        CommandFlags,
    }

    private static bool IsSERedis(ITypeSymbol? symbol, SERedis type)
    {
        static string NameOf(SERedis type) => type switch
        {
            SERedis.CommandFlags => nameof(SERedis.CommandFlags),
            _ => type.ToString(),
        };

        if (symbol is INamedTypeSymbol named && named.Name == NameOf(type))
        {
            // looking likely; check namespace
            if (named.ContainingNamespace is
                {
                    Name: "Redis", ContainingNamespace:
                    {
                        Name: "StackExchange",
                        ContainingNamespace.IsGlobalNamespace: true,
                    }
                })
            {
                return true;
            }

            // if the type doesn't resolve: we're going to need to trust it
            if (named.TypeKind == TypeKind.Error) return true;
        }

        return false;
    }

    private static string GetName(ITypeSymbol type)
    {
        if (type.ContainingType is null) return type.Name;
        var stack = new Stack<string>();
        while (true)
        {
            stack.Push(type.Name);
            if (type.ContainingType is null) break;
            type = type.ContainingType;
        }

        var sb = new StringBuilder(stack.Pop());
        while (stack.Count != 0)
        {
            sb.Append('.').Append(stack.Pop());
        }

        return sb.ToString();
    }

    [Conditional("DEBUG")]
    private static void AddNotes(ref string notes, string note)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            notes = note;
        }
        else
        {
            notes += "; " + note;
        }
    }

    private MethodTuple Transform(
        GeneratorSyntaxContext ctx,
        CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not IMethodSymbol method) return default;
        if (!(method is { IsPartialDefinition: true, PartialImplementationPart: null })) return default;

        string returnType, debugNote = "";
        if (method.ReturnsVoid)
        {
            returnType = "";
        }
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        else if (method.ReturnType is null)
        {
            return default;
        }
        else
        {
            returnType = GetFullName(method.ReturnType);
        }

        string ns = "", parentType = "";
        if (method.ContainingType is { } containingType)
        {
            parentType = GetName(containingType);
            ns = containingType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }
        else if (method.ContainingNamespace is { } containingNamespace)
        {
            ns = containingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        string value = method.Name.ToLowerInvariant();
        string? formatter = null, parser = null;
        foreach (var attrib in method.GetAttributes())
        {
            if (IsRESPite(attrib.AttributeClass, RESPite.RespCommandAttribute))
            {
                if (attrib.ConstructorArguments.Length == 1)
                {
                    if (attrib.ConstructorArguments[0].Value?.ToString() is { Length: > 0 } val)
                    {
                        value = val;
                    }
                }

                foreach (var tuple in attrib.NamedArguments)
                {
                    switch (tuple.Key)
                    {
                        case "Formatter":
                            formatter = tuple.Value.Value?.ToString();
                            AddNotes(ref debugNote, $"custom formatter: '{formatter}'");
                            break;
                        case "Parser":
                            parser = tuple.Value.Value?.ToString();
                            AddNotes(ref debugNote, $"custom parser: '{parser}'");
                            break;
                    }
                }

                break; // we don't expect another [RespCommand]
            }
        }

        var parameters = new List<ParameterTuple>(method.Parameters.Length);

        // get context from the available fields
        string? context = null;
        IParameterSymbol? contextParam = null;
        foreach (var param in method.Parameters)
        {
            if (IsRESPite(param.Type, RESPite.RespContext))
            {
                contextParam = param;
                context = param.Name;
                break;
            }
        }

        if (context is null)
        {
            AddNotes(ref debugNote, $"checking {method.ContainingType.Name} for fields");
            foreach (var member in method.ContainingType.GetMembers())
            {
                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    if (IsRESPite(field.Type, RESPite.RespContext))
                    {
                        AddNotes(ref debugNote, $"{field.Name} WAS match - {field.Type.Name}");
                        context = field.Name;
                        break;
                    }
                }
            }
        }

        if (context is null)
        {
            // get context from primary constructor (actually, we look at all constructors,
            // and just hope that the one that matches: works!)
            foreach (var ctor in method.ContainingType.Constructors)
            {
                if (ctor.IsStatic) continue;
                foreach (var param in ctor.Parameters)
                {
                    if (IsRESPite(param.Type, RESPite.RespContext))
                    {
                        context = param.Name;
                        break;
                    }
                }

                if (context is not null) break;
            }
        }

        if (context is null)
        {
            // look for indirect from parameter
            foreach (var param in method.Parameters)
            {
                if (IsIndirectRespContext(param.Type, out var memberName))
                {
                    contextParam = param;
                    context = $"{param.Name}.{memberName}";
                    break;
                }
            }
        }

        if (context is null)
        {
            // look for indirect from field
            foreach (var member in method.ContainingType.GetMembers())
            {
                if (member is IFieldSymbol { IsStatic: false } field &&
                    IsIndirectRespContext(field.Type, out var memberName))
                {
                    context = $"{field.Name}.{memberName}";
                    break;
                }
            }
        }

        // See whether instead of x (param, etc.) *being* a RespContext, it could be something that *provides*
        // a RespContext; this is especially useful for using punned structs (that just wrap a RespContext) to
        // narrow the methods into logical groups, i.e. "strings", "hashes", etc.
        static bool IsIndirectRespContext(ITypeSymbol type, out string memberName)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol { IsStatic: false } field
                    && IsRESPite(field.Type, RESPite.RespContext))
                {
                    memberName = field.Name;
                    return true;
                }
            }

            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false } prop
                    && IsRESPite(prop.Type, RESPite.RespContext) && prop.GetMethod is not null)
                {
                    memberName = prop.Name;
                    return true;
                }
            }

            memberName = "";
            return false;
        }

        if (context is null)
        {
            // last ditch, get context from properties
            foreach (var member in method.ContainingType.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false } prop
                    && IsRESPite(prop.Type, RESPite.RespContext) && prop.GetMethod is not null)
                {
                    context = prop.Name;
                    break;
                }
            }
        }

        foreach (var param in method.Parameters)
        {
            var flags = ParameterFlags.Parameter;
            if (IsKey(param)) flags |= ParameterFlags.Key;
            if (IsSERedis(param.Type, SERedis.CommandFlags))
            {
                flags |= ParameterFlags.CommandFlags;
                // magic pattern; we *demand* a method called Context that takes the flags
                context = $"Context({param.Name})";
            }
            else if (IsRESPite(param.Type, RESPite.RespContext))
            {
                // ignore it, but no extra flag
            }
            else if (contextParam is not null && SymbolEqualityComparer.Default.Equals(param, contextParam))
            {
                // ignore it, but no extra flag
            }
            else
            {
                flags |= ParameterFlags.Data;
            }

            string modifiers = param.RefKind switch
            {
                RefKind.None => "",
                RefKind.In => "in ",
                RefKind.Out => "out ",
                RefKind.Ref => "ref ",
                _ => "",
            };

            if (param.Ordinal == 0 && method.IsExtensionMethod)
            {
                modifiers = "this " + modifiers;
            }

            List<LiteralTuple>? literals = null;

            void AddLiteral(string token, LiteralFlags literalFlags)
            {
                (literals ??= new()).Add(new(token, literalFlags));
            }

            AddNotes(ref debugNote, $"checking {param.Name} for literals");
            foreach (var attrib in param.GetAttributes())
            {
                if (IsRESPite(attrib.AttributeClass, RESPite.RespPrefixAttribute))
                {
                    if (attrib.ConstructorArguments.Length == 1)
                    {
                        if (attrib.ConstructorArguments[0].Value?.ToString() is { Length: > 0 } val)
                        {
                            AddNotes(ref debugNote, $"prefix {val}");
                            AddLiteral(val, LiteralFlags.None);
                        }
                    }
                }

                if (IsRESPite(attrib.AttributeClass, RESPite.RespSuffixAttribute))
                {
                    if (attrib.ConstructorArguments.Length == 1)
                    {
                        if (attrib.ConstructorArguments[0].Value?.ToString() is { Length: > 0 } val)
                        {
                            AddNotes(ref debugNote, $"suffix {val}");
                            AddLiteral(val, LiteralFlags.Suffix);
                        }
                    }
                }
            }

            var literalArray = literals is null ? EasyArray<LiteralTuple>.Empty : new(literals.ToArray());
            parameters.Add(new(GetFullName(param.Type), param.Name, modifiers, flags, literalArray));
        }

        var syntax = (MethodDeclarationSyntax)ctx.Node;
        return new(
            ns,
            parentType,
            returnType,
            method.Name,
            value,
            new(parameters.ToArray()),
            TypeModifiers(method.ContainingType),
            syntax.Modifiers.ToString(),
            context ?? "",
            formatter,
            parser,
            debugNote);

        static string TypeModifiers(ITypeSymbol type)
        {
            foreach (var symbol in type.DeclaringSyntaxReferences)
            {
                var syntax = symbol.GetSyntax();
                if (syntax is TypeDeclarationSyntax typeDeclaration)
                {
                    var mods = typeDeclaration.Modifiers.ToString();
                    return syntax switch
                    {
                        InterfaceDeclarationSyntax => $"{mods} interface",
                        StructDeclarationSyntax => $"{mods} struct",
                        _ => $"{mods} class",
                    };
                }
            }

            return "class"; // wut?
        }
    }

    private bool IsKey(IParameterSymbol param)
    {
        if (param.Name.EndsWith("key", StringComparison.InvariantCultureIgnoreCase))
        {
            return true;
        }

        foreach (var attrib in param.GetAttributes())
        {
            if (IsRESPite(attrib.AttributeClass, RESPite.RespKeyAttribute)) return true;
        }

        return false;
    }

    private string GetVersion()
    {
        var asm = GetType().Assembly;
        if (asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false).FirstOrDefault() is
            AssemblyFileVersionAttribute { Version: { Length: > 0 } } version)
        {
            return version.Version;
        }

        return asm.GetName().Version?.ToString() ?? "??";
    }

    private static string CodeLiteral(string value)
        => SyntaxFactory
            .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value))
            .ToFullString();

    private void Generate(
        SourceProductionContext ctx,
        ImmutableArray<MethodTuple> methods)
    {
        if (methods.IsDefaultOrEmpty) return;

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        bool first;
        int indent = 0;

        // find the unique param types, so we can build helpers
        Dictionary<EasyArray<ParameterTuple>, (string Name,
                bool Shared)>
            formatters =
                new(FormatterComparer.Default);

        foreach (var method in methods.AsSpan())
        {
            if (method.Formatter is not null) continue; // using explicit formatter
            var count = DataParameterCount(method.Parameters);
            switch (count)
            {
                case 0: continue; // no parameter to consider
                case 1:
                    var p = FirstDataParameter(method.Parameters);
                    if (p.Literals.IsEmpty)
                    {
                        // no literals, and basic write scenario;consumer should add their own extension method
                        continue;
                    }

                    break;
            }

            // add a new formatter, or mark an existing formatter as shared
            var key = method.Parameters;
            if (!formatters.TryGetValue(key, out var existing))
            {
                formatters.Add(key, ($"__RespFormatter_{formatters.Count}", false));
            }
            else if (!existing.Shared)
            {
                formatters[key] = (existing.Name, true); // mark shared
            }
        }

        StringBuilder NewLine() => sb.AppendLine().Append(' ', Math.Max(indent * 4, 0));
        NewLine().Append("using global::RESPite;");
        foreach (var method in methods.AsSpan())
        {
            if (HasAnyFlag(method.Parameters, ParameterFlags.CommandFlags))
            {
                NewLine().Append("using global::RESPite.StackExchange.Redis;");
                break;
            }
        }

        NewLine().Append("using global::System;");
        NewLine().Append("using global::System.Threading.Tasks;");

        foreach (var grp in methods.GroupBy(l => (l.Namespace, l.TypeName, l.TypeModifiers)))
        {
            NewLine();
            int braces = 0;
            if (!string.IsNullOrWhiteSpace(grp.Key.Namespace))
            {
                NewLine().Append("namespace ").Append(grp.Key.Namespace);
                NewLine().Append("{");
                indent++;
                braces++;
            }

            if (!string.IsNullOrWhiteSpace(grp.Key.TypeName))
            {
                if (grp.Key.TypeName.Contains('.')) // nested types
                {
                    var tokens = grp.Key.TypeName.Split('.');
                    for (var i = 0; i < tokens.Length; i++)
                    {
                        var part = tokens[i];
                        if (i == tokens.Length - 1)
                        {
                            NewLine().Append(grp.Key.TypeModifiers).Append(' ').Append(part);
                        }
                        else
                        {
                            NewLine().Append("partial class ").Append(part);
                        }

                        NewLine().Append("{");
                        indent++;
                        braces++;
                    }
                }
                else
                {
                    NewLine().Append(grp.Key.TypeModifiers).Append(' ').Append(grp.Key.TypeName);
                    NewLine().Append("{");
                    indent++;
                    braces++;
                }
            }

            foreach (var method in grp)
            {
                if (method.DebugNotes is { Length: > 0 })
                {
                    NewLine().Append("/* ").Append(method.MethodName).Append(": ")
                        .Append(method.DebugNotes).Append(" */");
                }

                bool isSharedFormatter = false;
                string? formatter = method.Formatter
                                    ?? InbuiltFormatter(method.Parameters);
                if (formatter is null && formatters.TryGetValue(method.Parameters, out var tmp))
                {
                    formatter = $"{tmp.Name}.Default";
                    isSharedFormatter = tmp.Shared;
                }

                // perform string escaping on the generated value (this includes the quotes, note)
                var csValue = CodeLiteral(method.Command);

                WriteMethod(false);
                WriteMethod(true);

                void WriteMethod(bool asAsync)
                {
                    sb = NewLine().Append(asAsync ? RemovePartial(method.MethodModifiers) : method.MethodModifiers)
                        .Append(' ');
                    if (asAsync)
                    {
                        sb.Append(HasAnyFlag(method.Parameters, ParameterFlags.CommandFlags) ? "Task" : "ValueTask");
                        if (!string.IsNullOrWhiteSpace(method.ReturnType))
                        {
                            sb.Append('<').Append(method.ReturnType).Append('>');
                        }
                    }
                    else
                    {
                        sb.Append(string.IsNullOrEmpty(method.ReturnType) ? "void" : method.ReturnType);
                    }

                    sb.Append(' ').Append(method.MethodName).Append(asAsync ? "Async" : "").Append("(");
                    first = true;
                    foreach (var param in method.Parameters)
                    {
                        if ((param.Flags & ParameterFlags.Parameter) == 0) continue;
                        if (!first) sb.Append(", ");
                        first = false;

                        sb.Append(param.Modifiers).Append(param.Type).Append(' ').Append(param.Name);
                    }

                    var dataParameters = DataParameterCount(method.Parameters);
                    sb.Append(")");
                    indent++;

                    var parser = method.Parser ?? InbuiltParser(method.ReturnType, explicitSuccess: true);
                    bool useDirectCall = method.Context is { Length: > 0 } & formatter is { Length: > 0 } &
                                         parser is { Length: > 0 };

                    if (string.IsNullOrWhiteSpace(method.Context))
                    {
                        NewLine().Append("=> throw new NotSupportedException(\"No RespContext available\");");
                        useDirectCall = false;
                    }
                    else if (!(useDirectCall & asAsync))
                    {
                        sb = NewLine();
                        if (useDirectCall) sb.Append("// ");
                        sb.Append("=> ").Append(method.Context).Append(".Command(").Append(csValue).Append("u8");
                        if (dataParameters != 0)
                        {
                            sb.Append(", ");
                            WriteTuple(method.Parameters, sb, TupleMode.Values);

                            if (!string.IsNullOrWhiteSpace(formatter))
                            {
                                sb.Append(", ").Append(formatter);
                            }
                        }

                        sb.Append(asAsync ? ").Send" : ").Wait");
                        if (!string.IsNullOrWhiteSpace(method.ReturnType))
                        {
                            sb.Append('<').Append(method.ReturnType).Append('>');
                        }

                        sb.Append("(").Append(parser).Append(")");
                        if (asAsync && HasAnyFlag(method.Parameters, ParameterFlags.CommandFlags))
                        {
                            sb.Append(".AsTask()");
                        }

                        sb.Append(';');
                    }

                    if (useDirectCall) // avoid the intermediate step when possible
                    {
                        sb = NewLine().Append("=> ").Append(method.Context).Append(".Send")
                            .Append('<');
                        WriteTuple(
                            method.Parameters,
                            sb,
                            isSharedFormatter ? TupleMode.SyntheticNames : TupleMode.NamedTuple);
                        if (!string.IsNullOrWhiteSpace(method.ReturnType))
                        {
                            sb.Append(", ").Append(method.ReturnType);
                        }

                        sb.Append(">(").Append(csValue).Append("u8").Append(", ");
                        WriteTuple(method.Parameters, sb, TupleMode.Values);
                        sb.Append(", ").Append(formatter).Append(", ").Append(parser).Append(")");
                        if (asAsync)
                        {
                            sb.Append(HasAnyFlag(method.Parameters, ParameterFlags.CommandFlags)
                                ? ".AsTask()"
                                : ".AsValueTask()");
                        }
                        else
                        {
                            sb.Append(".Wait(");
                            if (HasAnyFlag(method.Parameters, ParameterFlags.CommandFlags))
                            {
                                // to avoid calling Context(flags) twice, we assume that this member will exist
                                sb.Append("SyncTimeout");
                            }
                            else
                            {
                                sb.Append(method.Context).Append(".SyncTimeout");
                            }

                            sb.Append(")");
                        }

                        sb.Append(";");
                    }

                    indent--;
                    NewLine();
                }
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                indent--;
                NewLine().Append("}");
            }

            NewLine();
        }

        foreach (var tuple in formatters)
        {
            var parameters = tuple.Key;
            var name = tuple.Value.Name;
            var names = tuple.Value.Shared ? TupleMode.SyntheticNames : TupleMode.NamedTuple;

            NewLine();
            sb = NewLine().Append("sealed file class ").Append(name)
                .Append(" : global::RESPite.Messages.IRespFormatter<");
            WriteTuple(parameters, sb, names);
            sb.Append('>');
            NewLine().Append("{");
            indent++;
            NewLine().Append("public static readonly ").Append(name).Append(" Default = new();");
            NewLine();

            sb = NewLine()
                .Append(
                    "public void Format(scoped ReadOnlySpan<byte> command, ref global::RESPite.Messages.RespWriter writer, in ");
            WriteTuple(parameters, sb, names);
            sb.Append(" request)");
            NewLine().Append("{");
            indent++;
            var count = DataParameterCount(parameters, out int literalCount);
            sb = NewLine().Append("writer.WriteCommand(command, ").Append(count + literalCount);
            sb.Append(");");

            void WritePrefix(ParameterTuple p) => WriteLiteral(p, false);
            void WriteSuffix(ParameterTuple p) => WriteLiteral(p, true);

            void WriteLiteral(ParameterTuple p, bool suffix)
            {
                LiteralFlags match = suffix ? LiteralFlags.Suffix : LiteralFlags.None;
                foreach (var literal in p.Literals.Span)
                {
                    if ((literal.Flags & LiteralFlags.Suffix) == match)
                    {
                        sb = NewLine().Append("writer.WriteBulkString(").Append(CodeLiteral(literal.Token)).Append("u8);");
                    }
                }
            }

            if (count == 1)
            {
                var p = FirstDataParameter(parameters);
                WritePrefix(p);
                sb = NewLine().Append("writer.");
                if (p.Type is "global::StackExchange.Redis.RedisValue" or "global::StackExchange.Redis.RedisKey")
                {
                    sb.Append("Write");
                }
                else
                {
                    sb.Append((p.Flags & ParameterFlags.Key) == 0 ? "WriteBulkString" : "WriteKey");
                }

                sb.Append("(request);");
                WriteSuffix(p);
            }
            else
            {
                int index = 0;
                foreach (var parameter in parameters.Span)
                {
                    if ((parameter.Flags & ParameterFlags.DataParameter) == ParameterFlags.DataParameter)
                    {
                        WritePrefix(parameter);
                        sb = NewLine().Append("writer.");
                        if (parameter.Type is "global::StackExchange.Redis.RedisValue"
                            or "global::StackExchange.Redis.RedisKey")
                        {
                            sb.Append("Write");
                        }
                        else
                        {
                            sb.Append((parameter.Flags & ParameterFlags.Key) == 0 ? "WriteBulkString" : "WriteKey");
                        }

                        sb.Append("(request.");
                        if (names == TupleMode.SyntheticNames)
                        {
                            sb.Append("Arg").Append(index);
                        }
                        else
                        {
                            sb.Append(parameter.Name);
                        }

                        sb.Append(");");
                        index++;
                        WriteSuffix(parameter);
                    }
                }

                Debug.Assert(index == count, "wrote all parameters");
            }

            indent--;
            NewLine().Append("}");
            indent--;
            NewLine().Append("}");
        }

        NewLine();
        ctx.AddSource(GetType().Name + ".generated.cs", sb.ToString());

        static void WriteTuple(
            EasyArray<ParameterTuple> parameters,
            StringBuilder sb,
            TupleMode mode)
        {
            var count = DataParameterCount(parameters);
            if (count == 0) return;
            if (count < 2)
            {
                var p = FirstDataParameter(parameters);
                sb.Append(mode == TupleMode.Values ? p.Name : p.Type);
                return;
            }

            sb.Append('(');
            int index = 0;
            foreach (var param in parameters.Span)
            {
                if ((param.Flags & ParameterFlags.DataParameter) != ParameterFlags.DataParameter)
                {
                    continue; // note don't increase index
                }

                if (index != 0) sb.Append(", ");

                switch (mode)
                {
                    case TupleMode.Values:
                        sb.Append(param.Name);
                        break;
                    case TupleMode.AnonTuple:
                        sb.Append(param.Type);
                        break;
                    case TupleMode.NamedTuple:
                        sb.Append(param.Type).Append(' ').Append(param.Name);
                        break;
                    case TupleMode.SyntheticNames:
                        sb.Append(param.Type).Append(" Arg").Append(index);
                        break;
                }

                index++;
            }

            sb.Append(')');
        }
    }

    private static bool HasAnyFlag(
        EasyArray<ParameterTuple> parameters,
        ParameterFlags any)
    {
        foreach (var p in parameters.Span)
        {
            if ((p.Flags & any) != 0) return true;
        }

        return false;
    }

    private static string? InbuiltFormatter(
        EasyArray<ParameterTuple> parameters)
    {
        if (DataParameterCount(parameters) == 1)
        {
            var p = FirstDataParameter(parameters);
            if (p.Literals.IsEmpty)
            {
                // can only use the inbuilt formatter if there are no literals
                return InbuiltFormatter(p.Type, (p.Flags & ParameterFlags.Key) != 0);
            }
        }

        return null;
    }

    private static ParameterTuple FirstDataParameter(
        EasyArray<ParameterTuple> parameters)
    {
        if (!parameters.IsEmpty)
        {
            foreach (var parameter in parameters.Span)
            {
                if ((parameter.Flags & ParameterFlags.DataParameter) == ParameterFlags.DataParameter)
                {
                    return parameter;
                }
            }
        }

        return Array.Empty<ParameterTuple>().First();
    }

    private static int DataParameterCount(
        EasyArray<ParameterTuple> parameters)
        => DataParameterCount(parameters, out _);

    private static int DataParameterCount(
        EasyArray<ParameterTuple> parameters, out int literalCount)
    {
        literalCount = 0;
        if (parameters.IsEmpty) return 0;
        int count = 0;
        foreach (var parameter in parameters.Span)
        {
            if ((parameter.Flags & ParameterFlags.DataParameter) == ParameterFlags.DataParameter)
            {
                if (!parameter.Literals.IsEmpty) literalCount += parameter.Literals.Length;
                count++;
            }
        }

        return count;
    }

    private const string RespFormattersPrefix = "global::RESPite.RespFormatters.";

    private static string? InbuiltFormatter(string type, bool isKey) => type switch
    {
        "string" => isKey ? (RespFormattersPrefix + "Key.String") : (RespFormattersPrefix + "Value.String"),
        "byte[]" => isKey ? (RespFormattersPrefix + "Key.ByteArray") : (RespFormattersPrefix + "Value.ByteArray"),
        "int" => RespFormattersPrefix + "Int32",
        "long" => RespFormattersPrefix + "Int64",
        "float" => RespFormattersPrefix + "Single",
        "double" => RespFormattersPrefix + "Double",
        "" => RespFormattersPrefix + "Empty",
        "global::StackExchange.Redis.RedisKey" => "global::RESPite.StackExchange.Redis.RespFormatters.RedisKey",
        "global::StackExchange.Redis.RedisValue" => "global::RESPite.StackExchange.Redis.RespFormatters.RedisValue",
        _ => null,
    };

    private const string RespParsersPrefix = "global::RESPite.RespParsers.";

    private static string? InbuiltParser(string type, bool explicitSuccess = false) => type switch
    {
        "" when explicitSuccess => RespParsersPrefix + "Success",
        "bool" => RespParsersPrefix + "Success",
        "string" => RespParsersPrefix + "String",
        "int" => RespParsersPrefix + "Int32",
        "long" => RespParsersPrefix + "Int64",
        "float" => RespParsersPrefix + "Single",
        "double" => RespParsersPrefix + "Double",
        "int?" => RespParsersPrefix + "NullableInt32",
        "long?" => RespParsersPrefix + "NullableInt64",
        "float?" => RespParsersPrefix + "NullableSingle",
        "double?" => RespParsersPrefix + "NullableDouble",
        "global::RESPite.RespParsers.ResponseSummary" => RespParsersPrefix + "ResponseSummary.Parser",
        "global::StackExchange.Redis.RedisKey" => "global::RESPite.StackExchange.Redis.RespParsers.RedisKey",
        "global::StackExchange.Redis.RedisValue" => "global::RESPite.StackExchange.Redis.RespParsers.RedisValue",
        "global::StackExchange.Redis.RedisValue[]" => "global::RESPite.StackExchange.Redis.RespParsers.RedisValueArray",
        "global::StackExchange.Redis.Lease<byte>" => "global::RESPite.StackExchange.Redis.RespParsers.BytesLease",
        _ => null,
    };

    private enum TupleMode
    {
        AnonTuple,
        NamedTuple,
        Values,
        SyntheticNames,
    }

    private static string RemovePartial(string modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers) || !modifiers.Contains("partial")) return modifiers;
        if (modifiers == "partial") return "";
        if (modifiers.StartsWith("partial ")) return modifiers.Substring(8);
        if (modifiers.EndsWith(" partial")) return modifiers.Substring(0, modifiers.Length - 8);
        return modifiers.Replace(" partial ", " ");
    }

    [Flags]
    private enum ParameterFlags
    {
        // ReSharper disable once UnusedMember.Local
        None = 0,
        Parameter = 1 << 0,
        Data = 1 << 1,
        DataParameter = Data | Parameter,
        Key = 1 << 2,
        CommandFlags = 1 << 3,
    }

    // compares whether a formatter can be shared, which depends on the key index and types (not names)
    private sealed class
        FormatterComparer
        : IEqualityComparer<EasyArray<ParameterTuple>>
    {
        private FormatterComparer() { }
        public static readonly FormatterComparer Default = new();

        public bool Equals(
            EasyArray<ParameterTuple> x,
            EasyArray<ParameterTuple> y)
        {
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
            {
                var px = x[i];
                var py = y[i];
                if (px.Type != py.Type || px.Flags != py.Flags) return false;
                // literals need to match by name too
                if (!px.Literals.SequenceEqual(py.Literals)) return false;
            }

            return true;
        }

        public int GetHashCode(
            EasyArray<ParameterTuple> obj)
        {
            var hash = obj.Length;
            foreach (var p in obj.Span)
            {
                hash ^= p.Type.GetHashCode() ^ (int)p.Flags ^ p.Literals.Length;
            }

            return hash;
        }
    }
}
