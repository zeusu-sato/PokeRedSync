namespace PokeRedSync;

public sealed record Bundle(uint Device, uint Hash, Dictionary<uint, uint> Vector, HashSet<(uint, uint)> Tombstones, byte[][] Records)
{
    public static Bundle? Parse(byte[][] records)
    {
        if (records.Length < 5) return null;
        var b = records[4];
        if (b.Length < 24 || !b.AsSpan(0, 4).SequenceEqual("PkSY"u8) || Pdb.U16(b, 4) != 1) return null;
        int n = Pdb.U16(b, 6), t = Pdb.U16(b, 16);
        if (n is < 1 or > 8 || t > 8 || b.Length != 24 + 8 * (n + t) || Pdb.U32(b, b.Length - 4) != Pdb.Crc(b.AsSpan(0, b.Length - 4))) return null;
        var vector = new Dictionary<uint, uint>(); var tombstones = new HashSet<(uint, uint)>();
        for (int i = 0; i < n; i++) if (!vector.TryAdd(Pdb.U32(b, 20 + i * 8), Pdb.U32(b, 24 + i * 8))) return null;
        for (int i = 0; i < t; i++) tombstones.Add((Pdb.U32(b, 20 + (n + i) * 8), Pdb.U32(b, 24 + (n + i) * 8)));
        var result = new byte[5][]; result[0] = b;
        for (int i = 0; i < 4; i++) result[i + 1] = Valid(records[i], i < 2 ? 0 : 1) ? records[i] : new byte[4];
        return new(Pdb.U32(b, 8), Pdb.U32(b, 12), vector, tombstones, result);
    }
    static bool Valid(byte[] b, int kind) => b.Length >= 20 && b.Length <= 32768 && b.AsSpan(0, 4).SequenceEqual("PkRS"u8) && Pdb.U16(b, 4) == 1 && Pdb.U16(b, 6) == kind && Pdb.U32(b, 12) == b.Length - 20 && Pdb.U32(b, 16) == Pdb.Crc(b, 16);
    public bool LessOrEqual(Bundle other) => Vector.All(x => x.Value <= other.Vector.GetValueOrDefault(x.Key));
}

public static class Selection
{
    public static List<Bundle> Choose(IReadOnlyList<Bundle> sources, Bundle? target)
    {
        var candidates = sources.Where(x => !ReferenceEquals(x, target) && !sources.Any(s => s.Tombstones.Contains((x.Device, x.Hash))) && (target == null || (x.Device != target.Device && x.Hash != target.Hash && !x.LessOrEqual(target)))).ToList();
        return candidates.Where((x, i) => !candidates.Where((_, j) => i != j).Any(y => x.LessOrEqual(y) && (!y.LessOrEqual(x) || x.Hash != y.Hash || candidates.IndexOf(y) < i))).Take(7).ToList();
    }
    public static byte[] Receive(List<Bundle> bundles)
    {
        byte[] header = new byte[12]; "PkRV"u8.CopyTo(header); Pdb.Put16(header, 4, 1); Pdb.Put16(header, 6, (ushort)bundles.Count); Pdb.Put32(header, 8, Pdb.Crc(header.AsSpan(0, 8)));
        return Pdb.Write("PokeRedRecv-PkRd", "recv", new[] { header }.Concat(bundles.SelectMany(b => b.Records)).ToArray());
    }
}


