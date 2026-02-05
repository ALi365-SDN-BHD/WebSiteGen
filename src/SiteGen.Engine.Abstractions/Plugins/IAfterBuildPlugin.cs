namespace SiteGen.Engine.Plugins;

public interface IAfterBuildPlugin
{
    void AfterBuild(BuildContext context);
}

