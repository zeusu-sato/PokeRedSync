using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PokeRedSync.Update;

public sealed record ReleaseAsset(string Name, string Url, long Size);
public sealed record UpdateRelease(string Tag, ReleaseAsset Zip, ReleaseAsset Checksums);

public static class ReleaseSelection
{
    private static readonly Regex TagPattern = new(@"\Av(20\d{2})\.((?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])[a-z])\z", RegexOptions.CultureInvariant);

    public static bool IsTag(string? tag) => tag is not null && TagPattern.IsMatch(tag);

    public static string? CurrentTag() => CurrentTag(Assembly.GetExecutingAssembly());

    public static string? CurrentTag(Assembly assembly)
    {
        string? tag = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "PokeRedSyncTag")?.Value;
        return IsTag(tag) ? tag : null;
    }

    public static UpdateRelease? Select(string json, string currentTag)
    {
        if (!IsTag(currentTag)) throw new ArgumentException("Invalid tag", nameof(currentTag));
        using var document = JsonDocument.Parse(json);
        UpdateRelease? latest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            string? tag = release.GetProperty("tag_name").GetString();
            if (!IsTag(tag) || string.CompareOrdinal(tag, currentTag) <= 0 ||
                (latest is not null && string.CompareOrdinal(tag, latest.Tag) <= 0)) continue;
            string zipName = $"PokeRedSync-{tag}-win-x64.zip";
            ReleaseAsset? zip = null, checksums = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (name != zipName && name != "SHA256SUMS.txt") continue;
                string? url = asset.GetProperty("browser_download_url").GetString();
                long size = asset.GetProperty("size").GetInt64();
                if (url is null || size < 0) continue;
                var item = new ReleaseAsset(name, url, size);
                if (name == zipName) zip = item; else checksums = item;
            }
            if (zip is not null && checksums is not null) latest = new(tag!, zip, checksums);
        }
        return latest;
    }
}
