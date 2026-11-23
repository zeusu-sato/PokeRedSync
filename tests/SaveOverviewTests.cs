using PokeRedSync;
using Xunit;

public class SaveOverviewTests
{
    static byte[][] Records(uint device, uint hash, uint clock, bool rejected = false)
    {
        byte[] sync = new byte[rejected ? 40 : 32];
        "PkSY"u8.CopyTo(sync); Pdb.Put16(sync, 4, 1); Pdb.Put16(sync, 6, 1);
        Pdb.Put32(sync, 8, device); Pdb.Put32(sync, 12, hash); Pdb.Put16(sync, 16, rejected ? (ushort)1 : (ushort)0);
        Pdb.Put32(sync, 20, device); Pdb.Put32(sync, 24, clock);
        if (rejected) { Pdb.Put32(sync, 28, 2); Pdb.Put32(sync, 32, 22); }
        Pdb.Put32(sync, sync.Length - 4, Pdb.Crc(sync.AsSpan(0, sync.Length - 4)));
        return new[] { Record(0, 1), Record(0, 2), Record(1, 1), Record(1, 2), sync };
    }

    [Fact]
    public void ReportsDeliveryAndReceivedAndBlocked()
    {
        string root = Path.Combine(Path.GetTempPath(), "pokered-overview-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string user in new[] { "A", "B" }) Directory.CreateDirectory(Path.Combine(root, user, "Backup"));
            var records = Records(1, 11, 1);
            byte[] pdb = Pdb.Write("PokeRedSave-PkRd", "save", records);
            Pdb.Put32(pdb, 40, 12345);
            File.WriteAllBytes(Path.Combine(root, "A", "Backup", "PokeRedSave-PkRd.pdb"), pdb);
            var rows = SaveOverview.Build(root);
            Assert.Equal(new DateTime(1904, 1, 1).AddSeconds(12345), rows[0].SavedAt);
            Assert.NotNull(rows[0].SyncedAt);
            Assert.Contains("まだ用意できていません", rows[1].Message);
            Assert.Equal(DeliveryStatus.Error, rows[1].Status);
            byte[] receive = Selection.Receive(new() { Bundle.Parse(records)! });
            Pdb.Put32(receive, 48, 0x50525359);
            Directory.CreateDirectory(Path.Combine(root, "B", "Install"));
            File.WriteAllBytes(Path.Combine(root, "B", "Install", "PokeRedRecv-PkRd.pdb"), receive);
            Assert.Contains("用意しました", SaveOverview.Build(root)[1].Message);
            File.Delete(Path.Combine(root, "B", "Install", "PokeRedRecv-PkRd.pdb"));
            File.WriteAllBytes(Path.Combine(root, "B", "Backup", "PokeRedRecv-PkRd.pdb"), receive);
            Assert.Contains("届いています", SaveOverview.Build(root)[1].Message);
            File.Delete(Path.Combine(root, "B", "Backup", "PokeRedRecv-PkRd.pdb"));
            Directory.CreateDirectory(Path.Combine(root, "B", "Install"));
            File.WriteAllBytes(Path.Combine(root, "B", "Install", "PokeRedRecv-PkRd.pdb"), new byte[] { 1 });
            Assert.Contains("置けません", SaveOverview.Build(root)[1].Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ReportsOtherSaveConditionsAndRunsSync()
    {
        string root = Path.Combine(Path.GetTempPath(), "pokered-overview-" + Guid.NewGuid().ToString("N"));
        try
        {
            string a = Path.Combine(root, "A", "Backup"), b = Path.Combine(root, "B", "Backup");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            string path = Path.Combine(b, "PokeRedSave-PkRd.pdb");
            Assert.Contains("まだセーブがありません", SaveOverview.Build(root)[0].Message);
            File.WriteAllBytes(path, new byte[] { 1 });
            Assert.Contains("セーブが読めません", SaveOverview.Build(root)[0].Message);
            File.WriteAllBytes(path, Pdb.Write("PokeRedSave-PkRd", "save", Records(2, 22, 1)[..4]));
            Assert.Contains("古い版のセーブなので届けられません", SaveOverview.Build(root)[0].Message);
            File.WriteAllBytes(path, Pdb.Write("PokeRedSave-PkRd", "save", Records(2, 22, 1)));
            Assert.Contains("まだ用意できていません", SaveOverview.Build(root)[0].Message);
            string exe = typeof(SaveOverview).Assembly.Location;
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet") { ArgumentList = { exe, "--root", root, "--once", "--log", Path.Combine(root, "log") }, UseShellExecute = false })!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("用意しました", SaveOverview.Build(root)[0].Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingRootAndMissingOrInvalidSavesAreSafe()
    {
        string root = Path.Combine(Path.GetTempPath(), "pokered-overview-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Empty(SaveOverview.Build(root));
            string backup = Path.Combine(root, "A", "Backup");
            Directory.CreateDirectory(backup);
            var missing = Assert.Single(SaveOverview.Build(root));
            Assert.Equal("A", missing.Device);
            Assert.Equal(DeliveryStatus.Error, missing.Status);
            Assert.Contains("まだセーブが", missing.Message);
            File.WriteAllBytes(Path.Combine(backup, "PokeRedSave-PkRd.pdb"), new byte[] { 1, 2, 3 });
            var invalid = Assert.Single(SaveOverview.Build(root));
            Assert.Equal(DeliveryStatus.Error, invalid.Status);
            Assert.Contains("セーブが読めません", invalid.Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static byte[] Record(int kind, uint sequence)
    {
        byte[] record = new byte[20];
        "PkRS"u8.CopyTo(record);
        Pdb.Put16(record, 4, 1);
        Pdb.Put16(record, 6, (ushort)kind);
        Pdb.Put32(record, 8, sequence);
        Pdb.Put32(record, 16, Pdb.Crc(record, 16));
        return record;
    }

    static byte[] SummaryRecord(byte species, byte level)
    {
        byte[] r = new byte[20 + 8 + 50 + 8 + 1];
        "PkRS"u8.CopyTo(r); Pdb.Put16(r, 4, 1); Pdb.Put16(r, 6, 1); Pdb.Put32(r, 8, 1);
        Pdb.Put32(r, 12, (uint)(r.Length - 20));
        "PLYR"u8.CopyTo(r.AsSpan(20)); Pdb.Put16(r, 24, 1); Pdb.Put16(r, 26, 50);
        r[28] = species == 0 ? (byte)0 : (byte)1; r[29] = species; r[63] = level;
        r[66] = 0x0c; r[67] = 0x3d; r[68] = 0x50;
        "LOCN"u8.CopyTo(r.AsSpan(78)); Pdb.Put16(r, 82, 1); Pdb.Put16(r, 84, 1);
        Pdb.Put32(r, 16, Pdb.Crc(r, 16)); return r;
    }

    static byte[] NamesPrc()
    {
        byte[] mons = new byte[8]; Pdb.Put16(mons, 0, 1); Pdb.Put16(mons, 2, 4); mons[4] = 0x0c; mons[5] = 0x3d; mons[6] = 0x50;
        byte[] maps = new byte[748]; Pdb.Put16(maps, 1, 744); maps[744] = 0x0c; maps[745] = 0x3d; maps[746] = 0x50;
        byte[] b = new byte[98 + mons.Length + maps.Length]; b[33] = 1; "applPkRd"u8.CopyTo(b.AsSpan(60)); Pdb.Put16(b, 76, 2);
        "Rmnu"u8.CopyTo(b.AsSpan(78)); Pdb.Put16(b, 82, 1); Pdb.Put32(b, 84, (uint)(98 + mons.Length));
        "Btnm"u8.CopyTo(b.AsSpan(88)); Pdb.Put16(b, 92, 0); Pdb.Put32(b, 94, 98); mons.CopyTo(b, 98); maps.CopyTo(b, 98 + mons.Length); return b;
    }

    [Fact]
    public void ExplainsExcludedSavesInSelectionOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "pokered-overview-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string user in new[] { "A", "B", "C" }) Directory.CreateDirectory(Path.Combine(root, user, "Backup"));
            Assert.Equal(string.Join(Environment.NewLine, "まだセーブが HotSync されていません", "B：まだセーブがありません", "C：まだセーブがありません"), SaveOverview.Build(root)[0].Message);
            void Save(string user, byte[][] records) => File.WriteAllBytes(Path.Combine(root, user, "Backup", "PokeRedSave-PkRd.pdb"), Pdb.Write("PokeRedSave-PkRd", "save", records));
            string Message() => SaveOverview.Build(root)[0].Message;
            Save("A", Records(1, 11, 2)); Save("B", Records(1, 22, 1));
            Assert.Contains("同じ端末として記録", Message());
            Save("B", Records(2, 11, 1)); Assert.Contains("同じ内容", Message());
            Save("B", Records(2, 22, 1)); Save("C", Records(1, 33, 1, true));
            Assert.Contains("以前に選ばなかった", Message());
            Save("C", Records(1, 33, 1));
            var older = Records(1, 22, 1); Pdb.Put32(older[4], 8, 2); Pdb.Put32(older[4], 28, Pdb.Crc(older[4].AsSpan(0, 28)));
            Save("B", older); Assert.Contains("この端末のほうが新しい", Message());
            Assert.DoesNotMatch(@"(?i)\b(bundle|hash|install|backup|id)\b|0x[0-9a-f]+", Message());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DisplaysSyntheticNamesLevelsAndTimes()
    {
        string root = Path.Combine(Path.GetTempPath(), "pokered-overview-" + Guid.NewGuid().ToString("N"));
        try
        {
            string backup = Path.Combine(root, "A", "Backup"); Directory.CreateDirectory(backup);
            string path = Path.Combine(backup, "PokeRedSave-PkRd.pdb");
            var records = Records(1, 11, 1); records[2] = SummaryRecord(1, 42);
            byte[] pdb = Pdb.Write("PokeRedSave-PkRd", "save", records); Pdb.Put32(pdb, 40, 12345); File.WriteAllBytes(path, pdb);
            DateTime time = new(2020, 1, 2, 3, 4, 6); File.SetLastWriteTime(path, time);
            var row = Assert.Single(SaveOverview.Build(root));
            Assert.Equal("ズベ", row.Player); Assert.Equal("（場所が分かりません）", row.Location); Assert.Equal("（ポケモンの名前が分かりません）", row.Pokemon);
            Assert.Equal(new DateTime(1904, 1, 1).AddSeconds(12345), row.SavedAt); Assert.Equal(time, row.SyncedAt);
            File.WriteAllBytes(Path.Combine(backup, "game.prc"), NamesPrc());
            row = Assert.Single(SaveOverview.Build(root)); Assert.Equal("ズベ", row.Location); Assert.Equal("ズベ　:L42", row.Pokemon);
            records[2] = SummaryRecord(1, 100); File.WriteAllBytes(path, Pdb.Write("PokeRedSave-PkRd", "save", records));
            Assert.Equal("ズベ　100", Assert.Single(SaveOverview.Build(root)).Pokemon);
            records[2] = SummaryRecord(0, 0); File.WriteAllBytes(path, Pdb.Write("PokeRedSave-PkRd", "save", records));
            Assert.Equal("なし", Assert.Single(SaveOverview.Build(root)).Pokemon);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DisplayRecordPrefersSuspendThenHighestSequence()
    {
        var records = new[] { Array.Empty<byte>(), Record(0, 10), Record(0, 20), Record(1, 1), Record(1, 30) };
        var bundle = new Bundle(1, 1, new(), new(), records);
        Assert.Same(records[3], bundle.DisplayRecord());
        records[3] = Array.Empty<byte>();
        Assert.Same(records[4], bundle.DisplayRecord());
        records[4][16] ^= 1;
        Assert.Same(records[2], bundle.DisplayRecord());
        records[1] = records[2] = Array.Empty<byte>();
        Assert.Null(bundle.DisplayRecord());
    }
}
