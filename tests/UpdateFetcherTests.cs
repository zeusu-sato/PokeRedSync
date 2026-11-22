using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PokeRedSync.Update;
using Xunit;

namespace PokeRedSync.Tests;

public sealed class UpdateFetcherTests
{
    private const string Tag = "v2026.1116a";
    private const string ZipName = "PokeRedSync-v2026.1116a-win-x64.zip";
    private const string BaseUrl = "https://github.com/zeusu-sato/PokeRedSync/releases/download/v2026.1116a/";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url)> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri));
            return Task.FromResult(send(request));
        }
    }

    private static byte[] Zip(params string[] names)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (string name in names) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("test"); }
        return stream.ToArray();
    }

    private static string Json(string zipUrl, long zipSize, long sumsSize) => JsonSerializer.Serialize(new[] { new
    {
        tag_name = Tag, draft = false, prerelease = false, assets = new[] {
            new { name = ZipName, browser_download_url = zipUrl, size = zipSize },
            new { name = "SHA256SUMS.txt", browser_download_url = BaseUrl + "SHA256SUMS.txt", size = sumsSize } }
    } });

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static async Task Run(string[]? names = null, string? url = null, string? sumsOverride = null, int sizeDelta = 0, bool success = false, string version = "1116a")
    {
        string parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            byte[] zip = Zip(names ?? new[] { "LICENSE", "PokeRedSync.exe" });
            string line = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() + "  " + ZipName;
            byte[] sums = Encoding.UTF8.GetBytes(sumsOverride?.Replace("{line}", line) ?? line + "\n");
            var handler = new Handler(req => req.RequestUri!.Host == "api.github.com"
                ? Response(Encoding.UTF8.GetBytes(Json(url ?? BaseUrl + ZipName, zip.Length + sizeDelta, sums.Length)))
                : Response(req.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt") ? sums : zip));
            var fetcher = new UpdateFetcher(handler, parent, _ => version);
            if (success)
            {
                var result = await fetcher.FetchAsync("v2026.1115a", _ => { }, default);
                Assert.Equal(Tag, result!.Tag);
                Assert.Equal("1116a", result.Version);
                Assert.Equal("staged", Path.GetFileName(result.Folder));
                string root = Path.GetDirectoryName(result.Folder)!;
                Assert.Equal(parent, Path.GetDirectoryName(root));
                Assert.StartsWith("PokeRedSync-update-", Path.GetFileName(root));
                Assert.Equal(new[] { "LICENSE", "PokeRedSync.exe" }, Directory.GetFiles(result.Folder).Select(Path.GetFileName).OrderBy(x => x).ToArray());
                Assert.All(Directory.GetFiles(result.Folder), file => Assert.Equal("test", File.ReadAllText(file)));
                Assert.Equal(new[] { result.Folder }, Directory.GetFileSystemEntries(root));
                Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
                Assert.Equal(new[] { "https://api.github.com/repos/zeusu-sato/PokeRedSync/releases", BaseUrl + "SHA256SUMS.txt", BaseUrl + ZipName }, handler.Requests.Select(r => r.Url));
            }
            else
            {
                await Assert.ThrowsAnyAsync<Exception>(() => fetcher.FetchAsync("v2026.1115a", _ => { }, default));
                Assert.Empty(Directory.GetFileSystemEntries(parent));
            }
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact] public async Task LatestReturnsNull()
    {
        var handler = new Handler(_ => Response(Encoding.UTF8.GetBytes("[]")));
        var fetcher = new UpdateFetcher(handler);
        Assert.Null(await fetcher.FetchAsync(Tag, _ => { }, default));
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }
    [Fact] public Task ValidPackage() => Run(success: true);
    [Theory]
    [InlineData("http://github.com/zeusu-sato/PokeRedSync/releases/download/x")]
    [InlineData("https://example.com/zeusu-sato/PokeRedSync/releases/download/x")]
    public Task RejectsUrls(string url) => Run(url: url);
    [Fact] public Task RejectsOtherUser() => Run(url: BaseUrl.Replace("zeusu-sato", "another-user"));
    [Fact] public Task RejectsVersionMismatch() => Run(version: "wrong");
    [Fact] public Task RejectsSubfolder() => Run(new[] { "LICENSE", "folder/PokeRedSync.exe" });
    [Fact] public Task RejectsOtherRepository() => Run(url: BaseUrl.Replace("PokeRedSync/", "Other/"));
    [Fact] public Task RejectsSimilarRepository() => Run(url: BaseUrl.Replace("PokeRedSync/", "PokeRedSync2/"));
    [Fact] public Task RejectsUserInfo() => Run(url: new UriBuilder(BaseUrl) { UserName = "user" }.Uri.AbsoluteUri);
    [Theory]
    [InlineData("")]
    [InlineData("{line}\n{line}\n")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  PokeRedSync-v2026.1116a-win-x64.zip\n")]
    public Task RejectsBadChecksums(string sums) => Run(sumsOverride: sums);
    [Fact] public Task RejectsSizeMismatch() => Run(sizeDelta: 1);
    [Theory]
    [InlineData("extra")]
    [InlineData("folder/")]
    [InlineData("../x")]
    public Task RejectsExtraEntry(string name) => Run(new[] { "LICENSE", "PokeRedSync.exe", name });
    [Fact] public Task RejectsMissingEntry() => Run(new[] { "LICENSE" });

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, true)]
    [InlineData(4, true, true)]
    [InlineData(5, true, true)]
    [InlineData(1, false, false)]
    [InlineData(6, true, false)]
    public async Task Redirects(int count, bool https, bool success)
    {
        string parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            byte[] zip = Zip("LICENSE", "PokeRedSync.exe");
            byte[] sums = Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() + "  " + ZipName + "\n");
            int redirects = 0;
            var handler = new Handler(req =>
            {
                if (req.RequestUri!.Host == "api.github.com") return Response(Encoding.UTF8.GetBytes(Json(BaseUrl + ZipName, zip.Length, sums.Length)));
                if (req.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt")) return Response(sums);
                if (redirects++ < count) return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri((https ? "https" : "http") + "://example.test/zip") } };
                return Response(zip);
            });
            var fetcher = new UpdateFetcher(handler, parent, _ => "1116a");
            if (success) Assert.NotNull(await fetcher.FetchAsync("v2026.1115a", _ => { }, default));
            else { await Assert.ThrowsAnyAsync<Exception>(() => fetcher.FetchAsync("v2026.1115a", _ => { }, default)); Assert.Empty(Directory.GetFileSystemEntries(parent)); }
        }
        finally { Directory.Delete(parent, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsOversizedApi(bool contentLength)
    {
        byte[] bytes = new byte[1024 * 1024 + 1];
        var handler = new Handler(_ =>
        {
            var response = Response(bytes);
            if (!contentLength) response.Content.Headers.ContentLength = null;
            return response;
        });
        await Assert.ThrowsAnyAsync<Exception>(() => new UpdateFetcher(handler).FetchAsync("v2026.1115a", _ => { }, default));
    }
}
