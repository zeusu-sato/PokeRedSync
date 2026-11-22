using PokeRedSync.Update;
using Xunit;

public class UpdateHandoverTests
{
    private sealed class Platform : IUpdateHandoverPlatform
    {
        public bool Ready, ThrowRestore;
        public int? ExitCode;
        public string? Updated, Failed;
        public readonly List<string> Calls = [];
        public void ResetReady() => Calls.Add("reset");
        public void SetUpdatedFrom(string tag) { Calls.Add("updated"); Updated = tag; }
        public void ClearUpdatedFrom() { Calls.Add("clear"); Updated = null; }
        public void SetFailedTag(string tag) { Calls.Add("failed"); Failed = tag; }
        public object StartTray(string folder, Action<string> log) { Calls.Add("start"); return new object(); }
        public HandoverResult WaitReady(object process, TimeSpan timeout) { Calls.Add("wait"); Assert.Equal(TimeSpan.FromSeconds(60), timeout); return new(Ready, TimeSpan.FromSeconds(2), ExitCode); }
        public void Stop(object process) => Calls.Add("stop");
        public void Restore(string folder) { Calls.Add("restore"); if (ThrowRestore) throw new IOException(); }
    }

    [Fact]
    public void SuccessKeepsMarkerAndCleansStaged()
    {
        string root = Path.Combine(Path.GetTempPath(), "PokeRedSync-update-" + Guid.NewGuid());
        string folder = Path.Combine(root, "staged"); Directory.CreateDirectory(folder);
        try
        {
            var platform = new Platform { Ready = true };
            Assert.True(new UpdateHandover(platform).Run("folder", new("new", "new", folder), "old", _ => { }));
            Assert.Equal("old", platform.Updated);
            Assert.Equal(new[] { "reset", "updated", "start", "wait" }, platform.Calls);
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureRestartsPreviousEvenIfRestoreFails(bool throwRestore)
    {
        var platform = new Platform { ThrowRestore = throwRestore };
        Assert.False(new UpdateHandover(platform).Run("folder", new("new", "new", "staged"), "old", _ => { }));
        Assert.Equal(new[] { "reset", "updated", "start", "wait", "stop", "clear", "restore", "failed", "start" }, platform.Calls);
        Assert.Null(platform.Updated);
        Assert.Equal("new", platform.Failed);
    }
    [Theory]
    [InlineData(null, "60 秒以内に準備完了なし")]
    [InlineData(3, "新しい版が 2.0 秒で終了（終了コード 3）")]
    public void FailureLogsReason(int? code, string expected)
    {
        var messages = new List<string>();
        Assert.False(new UpdateHandover(new Platform { ExitCode = code }).Run("folder", new("new", "new", "staged"), "old", messages.Add));
        Assert.Contains(expected, messages);
    }
}

