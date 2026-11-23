namespace Erp.Api;

// Task-2 §1: dependency-free .env loader — runs at startup (before the builder), reads the
// repo-root .env (resolved upward from the working directory) into process environment variables
// ONLY for keys not already set. Missing file = no-op. No DotNetEnv package (per brief).
public static class EnvLoader
{
    public static void Load()
    {
        var path = FindDotEnv(Environment.CurrentDirectory);
        if (path is null) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            var key = trimmed[..eq].Trim();
            var value = Unquote(trimmed[(eq + 1)..].Trim());
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindDotEnv(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
            ? value[1..^1]
            : value;
}
