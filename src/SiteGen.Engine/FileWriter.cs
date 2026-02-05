using System.Text;

namespace SiteGen.Engine;

public static class FileWriter
{
    public static void WriteUtf8(string outputRoot, string relativePath, string content)
    {
        var path = Path.Combine(outputRoot, relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

