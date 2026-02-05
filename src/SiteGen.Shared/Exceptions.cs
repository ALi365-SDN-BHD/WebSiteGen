namespace SiteGen.Shared;

public class SiteGenException : Exception
{
    public SiteGenException(string message) : base(message)
    {
    }

    public SiteGenException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class ConfigException : SiteGenException
{
    public ConfigException(string message) : base(message)
    {
    }
}

public sealed class ContentException : SiteGenException
{
    public ContentException(string message) : base(message)
    {
    }

    public ContentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class RenderException : SiteGenException
{
    public RenderException(string message) : base(message)
    {
    }

    public RenderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

