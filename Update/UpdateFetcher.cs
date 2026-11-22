using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PokeRedSync.Update;

public sealed class UpdateFetcher : IUpdateFetcher
{
    private const string ApiUrl = "https://api.github.com/repos/zeusu-sato/PokeRedSync/releases";
    private const string DownloadPrefix = "/zeusu-sato/PokeRedSync/releases/download/";
    private readonly HttpClient client;
    private readonly string tempParent;
    private readonly Func<string, string?> productVersion;

    public UpdateFetcher(HttpMessageHandler? handler = null, string? tempParent = null, Func<string, string?>? productVersion = null)
    {
        client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PokeRedSync");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        this.tempParent = tempParent ?? Path.GetTempPath();
        this.productVersion = productVersion ?? (path => FileVersionInfo.GetVersionInfo(path).ProductVersion);
    }

    public async Task<StagedUpdate?> FetchAsync(string currentTag, Action<string> log, CancellationToken token)
    {
        using var api = await client.GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead, token);
        api.EnsureSuccessStatusCode();
        using var json = new MemoryStream();
        await CopyLimited(api.Content, json, 1024 * 1024, null, token);
        var release = ReleaseSelection.Select(Encoding.UTF8.GetString(json.ToArray()), currentTag);
        if (release is null) { log($"更新の確認: 最新版です {currentTag}"); return null; }
        log($"更新の確認: 新しい版 {release.Tag}");
        ValidateUrl(release.Zip.Url);
        ValidateUrl(release.Checksums.Url);
        string root = Path.Combine(tempParent, "PokeRedSync-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var sums = new MemoryStream();
            await Download(release.Checksums, sums, 64 * 1024, token);
            string zipPath = Path.Combine(root, "package.zip");
            await using (var zipFile = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await Download(release.Zip, zipFile, 400L * 1024 * 1024, token);
            log($"更新のダウンロード: 完了 {release.Tag} {release.Zip.Size / 1024d / 1024d:F1} MB");
            string pattern = "^[0-9a-f]{64}  " + Regex.Escape(release.Zip.Name) + "$";
            string[] matches = Encoding.UTF8.GetString(sums.ToArray()).Split('\n')
                .Where(line => Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant)).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Invalid checksum list");
            await using (var input = File.OpenRead(zipPath))
            {
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
                if (!string.Equals(hash, matches[0][..64], StringComparison.Ordinal)) throw new InvalidDataException("Checksum mismatch");
            }
            log("更新の照合: 一致");
            string staged = Path.Combine(root, "staged");
            Directory.CreateDirectory(staged);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var names = new HashSet<string>(StringComparer.Ordinal) { "LICENSE", "PokeRedSync.exe" };
                if (archive.Entries.Count != 2 || archive.Entries.Any(e => !names.Remove(e.FullName)) || names.Count != 0)
                    throw new InvalidDataException("Invalid package contents");
                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length > 600L * 1024 * 1024 - total) throw new InvalidDataException("Package too large");
                    await using var source = entry.Open();
                    await using var destination = new FileStream(Path.Combine(staged, entry.FullName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    total += await CopyLimited(source, destination, 600L * 1024 * 1024 - total, token);
                }
            }
            string version = release.Tag.Split('.')[1];
            foreach (string name in new[] { "PokeRedSync.exe" })
                if (!string.Equals(productVersion(Path.Combine(staged, name)), version, StringComparison.Ordinal)) throw new InvalidDataException("Product version mismatch");
            File.Delete(zipPath);
            log("更新の展開: 完了");
            return new StagedUpdate(release.Tag, version, staged);
        }
        catch
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length != 0 || !uri.IsDefaultPort ||
            !uri.AbsolutePath.StartsWith(DownloadPrefix, StringComparison.Ordinal) ||
            !url.StartsWith("https://github.com" + DownloadPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid download URL");
    }

    private async Task Download(ReleaseAsset asset, Stream destination, long limit, CancellationToken token)
    {
        Uri uri = new(asset.Url);
        for (int redirects = 0; ; redirects++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirects >= 5 || response.Headers.Location is null) throw new InvalidDataException("Invalid redirect");
                uri = new Uri(uri, response.Headers.Location);
                if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0) throw new InvalidDataException("Invalid redirect URL");
                continue;
            }
            response.EnsureSuccessStatusCode();
            await CopyLimited(response.Content, destination, limit, asset.Size, token);
            return;
        }
    }

    private static async Task CopyLimited(HttpContent content, Stream destination, long limit, long? expected, CancellationToken token)
    {
        if (content.Headers.ContentLength is long length && length > limit) throw new InvalidDataException("Download too large");
        if (expected > limit) throw new InvalidDataException("Download too large");
        await using var input = await content.ReadAsStreamAsync(token);
        long size = await CopyLimited(input, destination, limit, token);
        if (expected is not null && size != expected) throw new InvalidDataException("Download size mismatch");
    }

    private static async Task<long> CopyLimited(Stream input, Stream output, long limit, CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Content too large");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return total;
    }
}
