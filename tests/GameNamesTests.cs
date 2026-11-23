using PokeRedSync;
using Xunit;

public class GameNamesTests
{
    static byte[] Prc(byte value = 1)
    {
        byte[] mons = new byte[12]; Pdb.Put16(mons, 0, 3); Pdb.Put16(mons, 2, 8); Pdb.Put16(mons, 6, 10); mons[8] = value; mons[9] = 0x50; mons[10] = 9; mons[11] = 0x50;
        byte[] maps = new byte[748]; Pdb.Put16(maps, 1, 744); maps[744] = value; maps[745] = 3; maps[746] = 0x50;
        byte[] b = new byte[98 + mons.Length + maps.Length]; b[33] = 1; "applPkRd"u8.CopyTo(b.AsSpan(60)); Pdb.Put16(b, 76, 2);
        "Rmnu"u8.CopyTo(b.AsSpan(78)); Pdb.Put16(b, 82, 1); Pdb.Put32(b, 84, (uint)(98 + mons.Length));
        "Btnm"u8.CopyTo(b.AsSpan(88)); Pdb.Put16(b, 92, 0); Pdb.Put32(b, 94, 98); mons.CopyTo(b, 98); maps.CopyTo(b, 98 + mons.Length); return b;
    }
    [Fact] public void ReadsNamesAndChecksBounds()
    {
        var names = GameNames.Load(Prc()); Assert.NotNull(names);
        Assert.Equal(new byte[] { 1 }, names.Mon(1)); Assert.Equal(new byte[] { 9 }, names.Mon(3));
        Assert.Null(names.Mon(0)); Assert.Null(names.Mon(2)); Assert.Null(names.Mon(4)); Assert.Null(names.Mon(191));
        Assert.Equal(new byte[] { 1, 3 }, names.Map(0)); Assert.Null(names.Map(1)); Assert.Null(names.Map(248)); Assert.Null(names.Map(255));
    }
    [Fact] public void RejectsInvalidOrMissingResources()
    {
        Assert.Null(GameNames.Load(new byte[78])); byte[] b = Prc(); b[78] = 0; Assert.Null(GameNames.Load(b));
        b = Prc(); Pdb.Put32(b, 84, (uint)b.Length + 1); Assert.Null(GameNames.Load(b));
        b = Prc(); b[98 + 11] = 1; Assert.Null(GameNames.Load(b)!.Mon(3));
    }
    [Fact] public void FindsPreferredUserAndFallsBack()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            string a = Path.Combine(root, "a", "Backup"), b = Path.Combine(root, "b", "Backup"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            File.WriteAllBytes(Path.Combine(a, "game.prc"), Prc(1)); File.WriteAllBytes(Path.Combine(b, "game.prc"), Prc(2));
            Assert.Equal(new byte[] { 2 }, GameNames.Find(root, Path.Combine(root, "b"))!.Mon(1));
            byte[] invalid = Prc(); invalid[78] = 0; File.WriteAllBytes(Path.Combine(b, "game.prc"), invalid);
            Assert.Equal(new byte[] { 1 }, GameNames.Find(root, Path.Combine(root, "b"))!.Mon(1));
            Assert.Null(GameNames.Find(Path.Combine(root, "missing"), null));
        }
        finally { Directory.Delete(root, true); }
    }
}

