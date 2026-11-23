namespace PokeRedSync;

public sealed record SaveSummary(byte[] Name, byte Map, byte Species, byte Level)
{
    public static SaveSummary? Read(byte[] record)
    {
        const int nameLength = 6;
        if (record.Length < 20 || !record.AsSpan(0, 4).SequenceEqual("PkRS"u8)) return null;
        byte[]? name = null; byte? map = null; byte species = 0, level = 0;
        for (int p = 20; p < record.Length;)
        {
            if (record.Length - p < 8) return null;
            var tag = record.AsSpan(p, 4); int version = Pdb.U16(record, p + 4), n = Pdb.U16(record, p + 6);
            p += 8;
            if (n > record.Length - p) return null;
            if (tag.SequenceEqual("PLYR"u8))
            {
                int tail = 2 + 2 * nameLength + (version >= 2 ? 1 : 0);
                if (version == 0 || n < tail + 36 || name != null) return null;
                if (record[p] != 0) { species = record[p + 1]; level = record[p + 35]; }
                var bytes = record.AsSpan(p + n - tail + 2, nameLength);
                int end = bytes.IndexOf((byte)0x50);
                name = bytes[..(end < 0 ? nameLength : end)].ToArray();
            }
            else if (tag.SequenceEqual("LOCN"u8))
            {
                if (version == 0 || n == 0 || map.HasValue) return null;
                map = record[p];
            }
            p += n;
        }
        return name != null && map.HasValue ? new(name, map.Value, species, level) : null;
    }
}
