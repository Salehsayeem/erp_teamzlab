namespace Erp.Api;

// ?status= filters — unknown values are a client error, not a silent empty list
public static class Query
{
    public static (bool ok, T? value) Enum<T>(string? raw) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw)) return (true, null);
        return System.Enum.TryParse<T>(raw, ignoreCase: true, out var value) ? (true, value) : (false, null);
    }
}
