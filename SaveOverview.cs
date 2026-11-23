namespace PokeRedSync;

public enum DeliveryStatus { Ready, None, Error }
public sealed record SaveOverviewRow(string Device, string? Player, string? Location, string? Pokemon,
    DateTime? SavedAt, DateTime? SyncedAt, DeliveryStatus Status, string Message);

public static class SaveOverview
{
    const string Save = "PokeRedSave-PkRd.pdb", Receive = "PokeRedRecv-PkRd.pdb";
    sealed record Snapshot(string Folder, byte[][]? Records, Bundle? Bundle, DateTime? Saved, DateTime? Synced, string? Error);

    public static IReadOnlyList<SaveOverviewRow> Build(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<SaveOverviewRow>();
        string[] users;
        try { users = Directory.GetDirectories(root).Where(d => Directory.Exists(Path.Combine(d, "Backup"))).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<SaveOverviewRow>(); }
        var snapshots = new List<Snapshot>();
        foreach (string user in users)
        {
            string path = Path.Combine(user, "Backup", Save);
            if (!File.Exists(path)) { snapshots.Add(new(user, null, null, null, null, "まだセーブが HotSync されていません")); continue; }
            DateTime? synced = null;
            try
            {
                synced = File.GetLastWriteTime(path);
                byte[] data = File.ReadAllBytes(path);
                var records = Pdb.Read(data, "PokeRedSave-PkRd", "save");
                uint seconds = Pdb.U32(data, 40);
                var bundle = Bundle.Parse(records);
                snapshots.Add(new(user, records, bundle, seconds == 0 ? null : new DateTime(1904, 1, 1).AddSeconds(seconds), synced,
                    bundle == null ? "同期の情報がない古い版のセーブです（ゲームを新しい版にして、もう一度セーブしてください）" : null));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { snapshots.Add(new(user, null, null, null, synced, "セーブが読めません（壊れているか、ほかのアプリのファイルです）")); }
        }
        var all = snapshots.Select(x => x.Bundle).OfType<Bundle>().ToList();
        var rows = new List<SaveOverviewRow>();
        foreach (var own in snapshots)
        {
            string? player = null, location = null, pokemon = null;
            byte[]? record = own.Bundle?.DisplayRecord() ?? (own.Records == null ? null : Bundle.DisplayRecord(own.Records));
            var summary = record == null ? null : SaveSummary.Read(record);
            string? ownError = own.Error;
            if (own.Records != null && summary == null && ownError == null)
                ownError = "セーブが読めません（壊れているか、ほかのアプリのファイルです）";
            if (summary != null)
            {
                player = GameText.Decode(summary.Name);
                GameNames? names = null;
                try { names = GameNames.Find(root, own.Folder); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                var map = names?.Map(summary.Map); var mon = names?.Mon(summary.Species);
                location = map == null ? "（場所が分かりません）" : GameText.Decode(map);
                pokemon = summary.Species == 0 ? "なし" : mon == null ? "（ポケモンの名前が分かりません）" : GameText.Decode(mon) + (summary.Level == 100 ? "　100" : $"　:L{summary.Level}");
            }
            var selected = Delivery.Plan(all, own.Bundle);
            DeliveryStatus status = selected.Count == 0 ? DeliveryStatus.None : DeliveryStatus.Ready;
            string message;
            if (selected.Count != 0)
            {
                string names = string.Join("、", snapshots.Where(x => x.Bundle != null && selected.Contains(x.Bundle)).Select(x => Path.GetFileName(x.Folder)));
                byte[] desired = Selection.Receive(selected); Pdb.Put32(desired, 48, 0x50525359);
                bool received = false, ready = false, blocked = false;
                try
                {
                    string backup = Path.Combine(own.Folder, "Backup", Receive);
                    if (File.Exists(backup))
                    {
                        try { var a = Pdb.Read(File.ReadAllBytes(backup), "PokeRedRecv-PkRd", "recv"); var b = Pdb.Read(desired, "PokeRedRecv-PkRd", "recv"); received = a.Length == b.Length && a.Zip(b).All(p => p.First.SequenceEqual(p.Second)); }
                        catch (InvalidDataException) { }
                    }
                    string install = Path.Combine(own.Folder, "Install", Receive);
                    if (!received && File.Exists(install))
                    {
                        byte[] existing = File.ReadAllBytes(install);
                        ready = existing.SequenceEqual(desired);
                        blocked = !ready && !Program.IsOurs(existing);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { blocked = true; }
                if (blocked) { status = DeliveryStatus.Error; message = "届け先に、このツールが作っていない同じ名前のファイルがあるため置けません"; }
                else if (received) message = $"{names}のセーブはこの端末に届いています。ゲームを起動すると使われます";
                else if (ready) message = $"{names}のセーブを用意しました。次の HotSync でこの端末に届きます";
                else { status = DeliveryStatus.Error; message = "まだ用意できていません（通知領域のメニューの「今すぐ処理」で用意します）"; }
            }
            else
            {
                var others = snapshots.Where(x => x != own).ToArray();
                message = others.Length == 0 ? "ほかの端末にセーブがありません" : string.Join(Environment.NewLine, others.Select(x => $"{Path.GetFileName(x.Folder)}：{Reason(x, own.Bundle, all)}"));
            }
            if (ownError != null) { status = DeliveryStatus.Error; message = ownError + Environment.NewLine + message; }
            rows.Add(new(Path.GetFileName(own.Folder), player, location, pokemon, own.Saved, own.Synced, status, message));
        }
        return rows;
    }
    static string Reason(Snapshot snapshot, Bundle? target, List<Bundle> all)
    {
        var source = snapshot.Bundle;
        if (source == null) return snapshot.Records != null ? "古い版のセーブなので届けられません" : snapshot.Error == "まだセーブが HotSync されていません" ? "まだセーブがありません" : "セーブが読めません";
        if (all.Any(x => x.Tombstones.Contains((source.Device, source.Hash)))) return "以前に選ばなかったセーブです";
        if (target != null && source.Device == target.Device) return "この端末と同じ端末として記録されています（区別できません）";
        if (target != null && source.Hash == target.Hash) return "同じ内容です";
        if (target != null && source.LessOrEqual(target)) return "この端末のほうが新しいか同じです";
        return "もっと新しいセーブがほかの端末にあります";
    }
}
