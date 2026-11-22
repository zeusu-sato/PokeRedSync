using PokeRedSync.Update;
using Xunit;

public class UpdateSchedulerTests
{
    private sealed class Fetcher : IUpdateFetcher
    {
        public List<string> Calls { get; } = [];
        public Task<StagedUpdate?> FetchAsync(string tag, Action<string> log, CancellationToken token)
        { Calls.Add(tag); return Task.FromResult<StagedUpdate?>(null); }
    }

    [Fact]
    public async Task WaitsAndUsesFailedTag()
    {
        var fetcher = new Fetcher();
        using var cancel = new CancellationTokenSource();
        List<TimeSpan> waits = [];
        Task Delay(TimeSpan time, CancellationToken token)
        {
            waits.Add(time);
            if (waits.Count == 3) { cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        }
        await new UpdateScheduler(fetcher, Delay).RunAsync("v2026.1122a", () => true, () => "v2026.1123a", (_, _) => Task.FromResult(false), _ => { }, cancel.Token);
        Assert.Equal(new[] { TimeSpan.FromMinutes(1), TimeSpan.FromHours(6), TimeSpan.FromHours(6) }, waits);
        Assert.Equal(new[] { "v2026.1123a", "v2026.1123a" }, fetcher.Calls);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("v2026.1122a", false)]
    public async Task DoesNotCheckWhenDisabledOrDevelopment(string? tag, bool enabled)
    {
        var fetcher = new Fetcher();
        await new UpdateScheduler(fetcher).RunAsync(tag, () => enabled, () => null, (_, _) => Task.FromResult(false), _ => { }, CancellationToken.None);
        Assert.Empty(fetcher.Calls);
    }
}
