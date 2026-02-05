using System.Security.Cryptography;
using System.Text;

namespace SiteGen.Engine.Incremental;

public static class HashUtil
{
    public static string Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Sha256Hex(bytes);
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return ToHexLower(hash);
    }

    public static string Sha256HexForDirectory(string rootDir)
    {
        if (!Directory.Exists(rootDir))
        {
            return Sha256Hex(string.Empty);
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var files = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
            .Select(p => new
            {
                FullPath = p,
                Relative = Path.GetRelativePath(rootDir, p).Replace('\\', '/')
            })
            .OrderBy(x => x.Relative, StringComparer.Ordinal)
            .ToList();

        foreach (var f in files)
        {
            var nameBytes = Encoding.UTF8.GetBytes(f.Relative);
            hasher.AppendData(nameBytes);
            hasher.AppendData(new byte[] { 0 });

            var content = File.ReadAllBytes(f.FullPath);
            hasher.AppendData(content);
            hasher.AppendData(new byte[] { 0 });
        }

        var digest = hasher.GetHashAndReset();
        return ToHexLower(digest);
    }

    private static string ToHexLower(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = GetHexLower(b >> 4);
            chars[i * 2 + 1] = GetHexLower(b & 0xF);
        }

        return new string(chars);
    }

    private static char GetHexLower(int value)
    {
        return (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));
    }
}

