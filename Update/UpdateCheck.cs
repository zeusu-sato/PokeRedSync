namespace PokeRedSync.Update;

public enum ManualCheckResult { Development, Latest, Declined, Installed, NotInstalled, Failed }

public static class UpdateCheck
{
    public static async Task<ManualCheckResult> RunAsync(string? currentTag, IUpdateFetcher fetcher,
        Func<StagedUpdate, bool> confirm, Func<StagedUpdate, CancellationToken, Task<bool>> tryInstall,
        Action<string> log, CancellationToken token)
    {
        if (!ReleaseSelection.IsTag(currentTag)) return ManualCheckResult.Development;
        StagedUpdate? staged;
        try { staged = await fetcher.FetchAsync(currentTag!, log, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { log("更新の確認: 失敗しました"); return ManualCheckResult.Failed; }
        if (staged is null) return ManualCheckResult.Latest;
        bool handedOver = false;
        try
        {
            if (!confirm(staged)) return ManualCheckResult.Declined;
            try
            {
                handedOver = await tryInstall(staged, token);
                return handedOver ? ManualCheckResult.Installed : ManualCheckResult.NotInstalled;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { log("更新の適用: 失敗しました"); return ManualCheckResult.NotInstalled; }
        }
        finally { if (!handedOver) UpdateScheduler.Cleanup(staged); }
    }

    public static string? Message(ManualCheckResult result, string? version) => result switch
    {
        ManualCheckResult.Development => "開発版のため更新を確認できません",
        ManualCheckResult.Latest => $"最新版です（{version}）",
        ManualCheckResult.Failed => "更新の確認に失敗しました",
        ManualCheckResult.NotInstalled => "更新を入れられませんでした。あとでもう一度お試しください",
        _ => null
    };
}
