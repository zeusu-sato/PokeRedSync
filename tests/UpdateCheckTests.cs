using PokeRedSync.Update;
using Xunit;

public class UpdateCheckTests
{
    private sealed class Fetcher : IUpdateFetcher
    {
        public string? Baseline;
        public StagedUpdate? Result;
        public Exception? Error;
        public Task<StagedUpdate?> FetchAsync(string currentTag, Action<string> log, CancellationToken token)
        {
            Baseline = currentTag;
            if (Error is not null) throw Error;
            return Task.FromResult(Result);
        }
    }
    private static Task<ManualCheckResult> Run(string? tag, Fetcher fetcher, bool confirm = true,
        Func<StagedUpdate, CancellationToken, Task<bool>>? install = null, Action<string>? log = null) =>
        UpdateCheck.RunAsync(tag, fetcher, _ => confirm, install ?? ((_, _) => Task.FromResult(true)), log ?? (_ => { }), CancellationToken.None);

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task DevelopmentDoesNotFetch(string? tag)
    {
        var fetcher = new Fetcher();
        Assert.Equal(ManualCheckResult.Development, await Run(tag, fetcher));
        Assert.Null(fetcher.Baseline);
    }
    [Fact]
    public async Task LatestUsesCurrentTagAsBaseline()
    {
        var fetcher = new Fetcher();
        Assert.Equal(ManualCheckResult.Latest, await Run("v2026.0101a", fetcher));
        Assert.Equal("v2026.0101a", fetcher.Baseline);
    }
    [Fact]
    public async Task FetchFailureIsLogged()
    {
        var logs = new List<string>();
        Assert.Equal(ManualCheckResult.Failed, await Run("v2026.0101a", new Fetcher { Error = new IOException() }, log: logs.Add));
        Assert.Contains("更新の確認: 失敗しました", logs);
    }
    [Fact]
    public async Task CancellationIsPropagated()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run("v2026.0101a", new Fetcher { Error = new OperationCanceledException() }));
    }
    [Theory]
    [InlineData("decline", ManualCheckResult.Declined, false)]
    [InlineData("success", ManualCheckResult.Installed, true)]
    [InlineData("failure", ManualCheckResult.NotInstalled, false)]
    [InlineData("exception", ManualCheckResult.NotInstalled, false)]
    public async Task InstallBranchesManageStagedFolder(string scenario, ManualCheckResult expected, bool remains)
    {
        string root = Path.Combine(Path.GetTempPath(), "PokeRedSync-update-" + Guid.NewGuid());
        string folder = Path.Combine(root, "staged"); Directory.CreateDirectory(folder);
        try
        {
            var fetcher = new Fetcher { Result = new("v2026.0102a", "2026.0102a", folder) };
            var logs = new List<string>();
            bool called = false;
            Task<bool> Install(StagedUpdate _, CancellationToken __)
            {
                called = true;
                if (scenario == "exception") throw new IOException();
                return Task.FromResult(scenario == "success");
            }
            Assert.Equal(expected, await Run("v2026.0101a", fetcher, scenario != "decline", Install, logs.Add));
            Assert.Equal(scenario != "decline", called);
            Assert.Equal(remains, Directory.Exists(root));
            if (scenario == "exception") Assert.Contains("更新の適用: 失敗しました", logs);
            Assert.Equal("v2026.0101a", fetcher.Baseline);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void MessagesMatchResults()
    {
        Assert.Equal("開発版のため更新を確認できません", UpdateCheck.Message(ManualCheckResult.Development, "v"));
        Assert.Equal("最新版です（v）", UpdateCheck.Message(ManualCheckResult.Latest, "v"));
        Assert.Equal("更新の確認に失敗しました", UpdateCheck.Message(ManualCheckResult.Failed, "v"));
        Assert.Equal("更新を入れられませんでした。あとでもう一度お試しください", UpdateCheck.Message(ManualCheckResult.NotInstalled, "v"));
        Assert.Null(UpdateCheck.Message(ManualCheckResult.Declined, "v"));
        Assert.Null(UpdateCheck.Message(ManualCheckResult.Installed, "v"));
    }
}
