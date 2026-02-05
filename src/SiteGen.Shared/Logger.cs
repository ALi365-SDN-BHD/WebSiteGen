using System.Text.Json;

namespace SiteGen.Shared;

public enum LogLevel
{
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4
}

public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleLogger : ILogger
{
    private readonly LogLevel _minLevel;
    private readonly string _format;

    public ConsoleLogger(LogLevel minLevel)
    {
        _minLevel = minLevel;
        _format = "text";
    }

    public ConsoleLogger(LogLevel minLevel, string format)
    {
        _minLevel = minLevel;
        _format = string.IsNullOrWhiteSpace(format) ? "text" : format.Trim().ToLowerInvariant();
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    private void Write(LogLevel level, string message)
    {
        if (level < _minLevel)
        {
            return;
        }

        if (_format == "json")
        {
            var payload = new
            {
                ts = DateTimeOffset.UtcNow.ToString("O"),
                level = level.ToString(),
                msg = message
            };
            Console.Error.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        var prefix = level switch
        {
            LogLevel.Debug => "[debug]",
            LogLevel.Info => "[info]",
            LogLevel.Warn => "[warn]",
            LogLevel.Error => "[error]",
            _ => "[log]"
        };

        Console.Error.WriteLine($"{prefix} {message}");
    }
}
