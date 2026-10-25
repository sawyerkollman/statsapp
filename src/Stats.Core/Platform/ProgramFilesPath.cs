namespace Stats.Core.Platform;

/// <summary>Pure Windows-path checks for "is this under an admin-only install directory?". No filesystem and no
/// ACL calls, so the policy can be unit-tested on any OS. Separators are normalized to backslash, <c>..</c> is
/// resolved, and comparison is case-insensitive. UNC paths and anything that is not a drive-letter path are
/// rejected — they are not a Program Files install.</summary>
public static class ProgramFilesPath
{
    public static IReadOnlyList<string> DefaultRoots()
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            var normalized = Normalize(path);
            if (normalized is null) return;
            if (roots.Exists(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))) return;
            roots.Add(normalized);
        }

        // Hardcoded well-known locations so a check still means something when the process environment does not
        // report Program Files (tests, unusual hosts). Live environment values are added as well so a non-C:
        // Program Files directory is accepted on the machine that is actually running.
        Add(@"C:\Program Files");
        Add(@"C:\Program Files (x86)");
        Add(Environment.GetEnvironmentVariable("ProgramFiles"));
        Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
        Add(Environment.GetEnvironmentVariable("ProgramW6432"));
        try { Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch { /* unavailable */ }
        try { Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch { /* unavailable */ }
        return roots;
    }

    /// <summary>True when <paramref name="path"/> is a file or directory strictly inside one of
    /// <paramref name="roots"/> (the root directory itself does not count).</summary>
    public static bool IsUnder(string? path, IEnumerable<string>? roots = null)
    {
        var full = Normalize(path);
        if (full is null) return false;
        foreach (var root in roots ?? DefaultRoots())
        {
            var normalizedRoot = Normalize(root);
            if (normalizedRoot is null) continue;
            var prefix = normalizedRoot.EndsWith('\\') ? normalizedRoot : normalizedRoot + "\\";
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>True for a drive root such as <c>C:\</c>. Parent walks must stop here: a standard user can create
    /// <c>C:\installer</c> on many Windows installs, and an elevated process must not treat that as its own tree.</summary>
    public static bool IsDriveRoot(string? path)
    {
        var normalized = Normalize(path);
        return normalized is { Length: 3 } && normalized[1] == ':' && normalized[2] == '\\';
    }

    /// <summary>Canonical <c>X:\a\b</c> form, or null when the path is empty, relative, UNC, or escapes its drive
    /// via <c>..</c>.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var text = path.Trim().Replace('/', '\\');
        if (text.IndexOf('"') >= 0 || text.IndexOf('\0') >= 0) return null;
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal)) text = text[4..];
        if (text.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        if (text.Length < 3 || text[1] != ':' || text[2] != '\\' || !char.IsLetter(text[0])) return null;

        var parts = text.Split('\\');
        var stack = new List<string> { parts[0] };
        for (int i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (stack.Count <= 1) return null;
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }
        return stack.Count == 1 ? stack[0] + "\\" : string.Join("\\", stack);
    }
}
