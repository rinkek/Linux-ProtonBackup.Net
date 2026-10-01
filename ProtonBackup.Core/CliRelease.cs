using System.Text.RegularExpressions;

namespace ProtonBackup.Core;

public sealed record CliDownload(string Platform, string Url, string? Sha512);

public sealed record CliRelease(string Version, IReadOnlyList<CliDownload> Downloads)
{
    public CliDownload? For(string platform) =>
        Downloads.FirstOrDefault(d => string.Equals(Normalise(d.Platform), platform, StringComparison.OrdinalIgnoreCase));

    /// The table lists linux/x64, the download URL uses linux-x64.
    public static string Normalise(string platform) => platform.Replace('/', '-');
}

/// The version page is plain HTML with no machine-readable list (phase 1, test 7), but it is
/// very regular in shape: the version is in the h1 and each platform has its URL and SHA-512 in a table.
/// The parser is deliberately lenient so that small markup changes do not break it.
public static partial class CliVersionPage
{
    [GeneratedRegex(@"<h1[^>]*>\s*Proton\s+Drive\s+CLI\s+([0-9][0-9A-Za-z.\-]*)\s*</h1>", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern { get; }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowPattern { get; }

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellPattern { get; }

    [GeneratedRegex(@"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern { get; }

    [GeneratedRegex(@"\b([0-9a-f]{128})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sha512Pattern { get; }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern { get; }

    public static CliRelease? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var version = VersionPattern.Match(html);
        if (!version.Success) return null;

        var downloads = new List<CliDownload>();
        foreach (Match row in RowPattern.Matches(html))
        {
            var cells = CellPattern.Matches(row.Groups[1].Value);
            if (cells.Count < 2) continue;

            var platform = Text(cells[0].Groups[1].Value);
            var url = HrefPattern.Match(row.Groups[1].Value);
            if (platform.Length == 0 || !url.Success) continue;

            var checksum = Sha512Pattern.Match(row.Groups[1].Value);
            downloads.Add(new CliDownload(platform, url.Groups[1].Value.Trim(),
                checksum.Success ? checksum.Groups[1].Value.ToLowerInvariant() : null));
        }

        return downloads.Count == 0 ? null : new CliRelease(version.Groups[1].Value, downloads);
    }

    private static string Text(string html) => TagPattern.Replace(html, "").Trim();

    /// Compares versions as sequences of numbers, so 0.10.0 is newer than 0.9.0.
    public static bool IsNewer(string candidate, string current)
    {
        var left = Numbers(candidate);
        var right = Numbers(current);
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var a = i < left.Count ? left[i] : 0;
            var b = i < right.Count ? right[i] : 0;
            if (a != b) return a > b;
        }
        return false;
    }

    private static List<long> Numbers(string version) =>
        [.. version.Split('.', '-', '+').TakeWhile(part => part.Length > 0 && part.All(char.IsDigit)).Select(long.Parse)];
}
