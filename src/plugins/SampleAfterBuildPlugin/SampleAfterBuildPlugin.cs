using SiteGen.Engine.Plugins;

namespace SiteGen.Plugins.SampleAfterBuildPlugin;

[SiteGenPlugin]
public sealed class SampleAfterBuildPlugin : ISiteGenPlugin, IAfterBuildPlugin
{
    public string Name => "sample-after-build";

    public string Version => "0.1.0";

    public void AfterBuild(BuildContext context)
    {
    }
}
