using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Palsys.Server.Configuration;

internal static class YamlEnvironment
{
    private static readonly Regex EnvironmentKey = new("^[A-Z][A-Z0-9_]*$", RegexOptions.Compiled);

    public static void Load(string relativePath, ISet<string> processEnvironmentKeys)
    {
        var path = ResolvePath(relativePath);
        if (path is null) return;

        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
            return;

        foreach (var entry in mapping.Children)
        {
            var key = (entry.Key as YamlScalarNode)?.Value?.Trim();
            var value = (entry.Value as YamlScalarNode)?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(key)
                || string.IsNullOrWhiteSpace(value)
                || !EnvironmentKey.IsMatch(key)
                || key.Equals("DB_PASSWORD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Explicit process values remain authoritative over YAML. Values
            // loaded from legacy dotenv files are intentionally overridable.
            if (processEnvironmentKeys.Contains(key)) continue;
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public static string? ResolvePath(string relativePath)
    {
        foreach (var start in new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        })
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
