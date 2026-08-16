namespace Lones.SptManager.Core.Paths;

public static class GamePath
{
    public static string Normalize(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);

        var trimmed = relative.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Relative path is empty after normalize.", nameof(relative));
        }

        return trimmed;
    }

    public static bool EqualsNormalized(string left, string right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsUnderOrEqual(string relative, string prefix)
    {
        var path = Normalize(relative);
        var root = Normalize(prefix);
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static string Combine(string root, string relative)
        => Path.Combine(root, Normalize(relative).Replace('/', Path.DirectorySeparatorChar));
}
