namespace PokeRedSync;

public sealed class GameNames
{
    readonly byte[] mons, maps;
    GameNames(byte[] mons, byte[] maps) { this.mons = mons; this.maps = maps; }

    public static GameNames? Load(byte[] prc)
    {
        if (prc.Length < 78 || (prc[33] & 1) == 0 || !prc.AsSpan(60, 8).SequenceEqual("applPkRd"u8)) return null;
        int count = Pdb.U16(prc, 76), tableEnd = 78 + count * 10;
        if (tableEnd > prc.Length) return null;
        byte[]? Resource(ReadOnlySpan<byte> type, int id)
        {
            int start = -1, end = prc.Length;
            for (int i = 0; i < count; i++)
            {
                int p = 78 + i * 10; uint offset = Pdb.U32(prc, p + 6);
                if (offset < tableEnd || offset > prc.Length) return null;
                if (prc.AsSpan(p, 4).SequenceEqual(type) && Pdb.U16(prc, p + 4) == id) start = (int)offset;
            }
            if (start < 0) return null;
            for (int i = 0; i < count; i++)
            {
                int offset = (int)Pdb.U32(prc, 84 + i * 10);
                if (offset > start && offset < end) end = offset;
            }
            return prc[start..end];
        }
        var mons = Resource("Btnm"u8, 0); var maps = Resource("Rmnu"u8, 1);
        return mons == null || maps == null ? null : new(mons, maps);
    }
    static byte[]? Name(byte[] data, int offset)
    {
        if (offset <= 0 || offset >= data.Length) return null;
        int end = data.AsSpan(offset).IndexOf((byte)0x50);
        return end < 0 ? null : data.AsSpan(offset, end).ToArray();
    }
    public byte[]? Mon(byte species)
    {
        if (species is 0 or > 190 || mons.Length < 2) return null;
        int index = species - 1, count = Pdb.U16(mons, 0), p = 2 + index * 2;
        return index >= count || p + 2 > mons.Length ? null : Name(mons, Pdb.U16(mons, p));
    }
    public byte[]? Map(byte map)
    {
        int p = map * 3 + 1;
        return map >= 248 || p + 2 > maps.Length ? null : Name(maps, Pdb.U16(maps, p));
    }
    public static GameNames? Find(string root, string? preferUserFolder)
    {
        string? preferred = preferUserFolder == null ? null : Path.GetFullPath(preferUserFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string file in PalmApplication.Find(root).OrderByDescending(x => preferred != null && Path.GetFullPath(x).StartsWith(preferred, StringComparison.OrdinalIgnoreCase)))
        {
            try { var names = Load(File.ReadAllBytes(file)); if (names != null) return names; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }
}
