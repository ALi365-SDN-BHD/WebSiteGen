using System.Reflection;

namespace SiteGen.Cli.Commands;

public static class VersionCommand
{
    public static Task<int> RunAsync(ArgReader reader)
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version?.ToString() ?? "0.0.0";
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Console.WriteLine($"sitegen {info ?? version}");
        Console.WriteLine("runtime: native-aot");
        return Task.FromResult(0);
    }
}

