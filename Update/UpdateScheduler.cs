namespace PokeRedSync.Update;

public sealed record StagedUpdate(string Tag, string Version, string Folder);

public interface IUpdateFetcher
{
    Task<StagedUpdate?> FetchAsync(string currentTag, Action<string> log, CancellationToken token);
}

public sealed class UpdateScheduler(IUpdateFetcher fetcher, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    public async Task RunAsync(string? currentTag, Func<bool> enabled, Func<string?> failedTag,
        Func<StagedUpdate, CancellationToken, Task<bool>> tryInstall, Action<string> log, CancellationToken token)
    {
        if (!ReleaseSelection.IsTag(currentTag) || !enabled()) return;
        try
        {
            await delay(TimeSpan.FromMinutes(1), token);
            while (!token.IsCancellationRequested && enabled())
            {
                StagedUpdate? staged = null;
                bool handedOver = false;
                try
                {
                    string? failed = failedTag();
                    string baseline = ReleaseSelection.IsTag(failed) && string.CompareOrdinal(failed, currentTag) > 0 ? failed! : currentTag!;
                    try { staged = await fetcher.FetchAsync(baseline, log, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception) { log("更新の確認: 失敗しました"); }
                    if (staged is not null && string.CompareOrdinal(staged.Tag, baseline) > 0)
                    {
                        while (!token.IsCancellationRequested && enabled())
                        {
                            try
                            {
                                if (await tryInstall(staged, token)) { handedOver = true; return; }
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                            catch (Exception) { log("更新の適用: 失敗しました"); }
                            await delay(TimeSpan.FromMinutes(1), token);
                        }
                    }
                }
                finally { if (staged is not null && !handedOver) Cleanup(staged); }
                if (enabled()) await delay(TimeSpan.FromHours(6), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public static void Cleanup(StagedUpdate staged)
    {
        string? root = Path.GetDirectoryName(Path.GetFullPath(staged.Folder));
        if (root is null || !Path.GetFileName(root).StartsWith("PokeRedSync-update-", StringComparison.Ordinal)) return;
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
