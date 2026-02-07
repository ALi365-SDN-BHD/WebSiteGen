using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SiteGen.PluginSourceGenerator;

[Generator]
public sealed class PluginSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(static () => new PluginSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not PluginSyntaxReceiver receiver)
        {
            return;
        }

        var compilation = context.Compilation;
        var pluginInterface = compilation.GetTypeByMetadataName("SiteGen.Engine.Plugins.ISiteGenPlugin");
        var pluginAttribute = compilation.GetTypeByMetadataName("SiteGen.Engine.Plugins.SiteGenPluginAttribute");

        if (pluginInterface is null)
        {
            return;
        }

        var pluginTypes = new List<INamedTypeSymbol>();

        foreach (var candidate in receiver.Candidates)
        {
            var model = compilation.GetSemanticModel(candidate.SyntaxTree);
            if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            if (typeSymbol.TypeKind != TypeKind.Class)
            {
                continue;
            }

            if (typeSymbol.IsAbstract)
            {
                continue;
            }

            if (!ImplementsInterface(typeSymbol, pluginInterface))
            {
                continue;
            }

            var namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
            if (!namespaceName.StartsWith("SiteGen.Plugins."))
            {
                continue;
            }

            if (pluginAttribute is not null && !HasAttribute(typeSymbol, pluginAttribute))
            {
                continue;
            }

            pluginTypes.Add(typeSymbol);
        }

        var source = GenerateSource(pluginTypes);
        context.AddSource("GeneratedPluginSource.g.cs", source);
    }

    static bool ImplementsInterface(INamedTypeSymbol typeSymbol, INamedTypeSymbol interfaceSymbol)
    {
        foreach (var implemented in typeSymbol.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, interfaceSymbol))
            {
                return true;
            }
        }

        return false;
    }

    static bool HasAttribute(INamedTypeSymbol typeSymbol, INamedTypeSymbol attributeSymbol)
    {
        foreach (var attributeData in typeSymbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attributeData.AttributeClass, attributeSymbol))
            {
                return true;
            }
        }

        return false;
    }

    static string GenerateSource(IReadOnlyList<INamedTypeSymbol> pluginTypes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using SiteGen.Engine.Plugins;");
        builder.AppendLine();
        builder.AppendLine("namespace SiteGen.Engine.Plugins.Generated;");
        builder.AppendLine();
        builder.AppendLine("internal sealed class GeneratedPluginSource : IPluginSource");
        builder.AppendLine("{");
        builder.AppendLine("    public IEnumerable<ISiteGenPlugin> GetPlugins()");
        builder.AppendLine("    {");

        foreach (var pluginType in pluginTypes)
        {
            var displayName = pluginType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var typeName = displayName.Replace("global::", string.Empty);
            builder.Append("        yield return new ");
            builder.Append(typeName);
            builder.AppendLine("();");
        }

        builder.AppendLine("        yield break;");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    sealed class PluginSyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> Candidates { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is ClassDeclarationSyntax declaration)
            {
                Candidates.Add(declaration);
            }
        }
    }
}
