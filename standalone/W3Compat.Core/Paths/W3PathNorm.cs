namespace W3Compat.Paths;

/// <summary>与引擎无关的 War3 相对路径规范化（不含文件系统访问）。</summary>
public static class W3PathNorm
{
    /// <summary>统一分隔符并去掉前导斜杠。</summary>
    public static string NormalizeRelative(string? relPath)
    {
        if (string.IsNullOrEmpty(relPath))
            return string.Empty;

        string norm = relPath.Replace('\\', '/');
        while (norm.StartsWith('/'))
            norm = norm[1..];
        return norm;
    }

    /// <summary>
    /// 目录段首字母大写的尽力猜测：units/human/Footman → Units/Human/Footman。
    /// 文件名段保持原样。
    /// </summary>
    public static string TitleCaseSegments(string norm)
    {
        if (string.IsNullOrEmpty(norm))
            return string.Empty;

        string[] parts = norm.Split('/');
        var output = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (string.IsNullOrEmpty(part))
                continue;

            bool isDirectory = i < parts.Length - 1;
            if (isDirectory && part.Length > 0)
                output.Add(char.ToUpperInvariant(part[0]) + part[1..]);
            else
                output.Add(part);
        }

        return string.Join('/', output);
    }

    /// <summary>大写字母越多，越优先作为大小写不敏感匹配的规范名。</summary>
    public static int CaseSpecificity(string value)
    {
        int score = 0;
        foreach (char ch in value)
        {
            if (ch is >= 'A' and <= 'Z')
                score++;
        }

        return score;
    }

    /// <summary>拼接根目录与相对路径（统一为正斜杠风格的逻辑键，不访问磁盘）。</summary>
    public static string CacheKey(string root, string normalizedRelative)
    {
        string rootKey = root.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        string relKey = NormalizeRelative(normalizedRelative).ToLowerInvariant();
        return string.IsNullOrEmpty(relKey) ? rootKey : rootKey + "/" + relKey;
    }
}
