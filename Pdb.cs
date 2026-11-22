using System.Buffers.Binary;
using System.Text;

namespace PokeRedSync;

public static class Pdb
{
    public static ushort U16(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt16BigEndian(b[p..]);
    public static uint U32(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt32BigEndian(b[p..]);
    public static void Put16(Span<byte> b, int p, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b[p..], v);
    public static void Put32(Span<byte> b, int p, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b[p..], v);
    public static uint Crc(ReadOnlySpan<byte> b, int skip = -1)
    {
        uint c = 0xffffffff;
        for (int i = 0; i < b.Length; i++) { if (skip >= 0 && i >= skip && i < skip + 4) continue; c ^= b[i]; for (int j = 0; j < 8; j++) c = (c >> 1) ^ ((c & 1) != 0 ? 0xedb88320u : 0); }
        return c ^ 0xffffffff;
    }
    public static byte[][] Read(byte[] data, string name, string type)
    {
        if (data.Length < 78 || Encoding.ASCII.GetString(data, 0, 32).TrimEnd('\0') != name || Encoding.ASCII.GetString(data, 60, 4) != type || Encoding.ASCII.GetString(data, 64, 4) != "PkRd") throw new InvalidDataException("Invalid PDB header");
        int n = U16(data, 76), end = 78 + n * 8 + 2;
        if (end > data.Length) throw new InvalidDataException("Invalid PDB record list");
        var records = new byte[n][];
        for (int i = 0; i < n; i++) { uint a = U32(data, 78 + i * 8), z = i + 1 == n ? (uint)data.Length : U32(data, 86 + i * 8); if (a < end || a > z || z > data.Length) throw new InvalidDataException("Invalid PDB record offset"); records[i] = data[(int)a..(int)z]; }
        return records;
    }
    public static byte[] Write(string name, string type, IReadOnlyList<byte[]> records, ushort attributes = 0)
    {
        int offset = 78 + records.Count * 8 + 2;
        var data = new byte[checked(offset + records.Sum(r => r.Length))];
        Encoding.ASCII.GetBytes(name).CopyTo(data, 0); Put16(data, 32, attributes); Put16(data, 34, 1);
        // Fixed timestamps make identical output byte-for-byte stable.
        Put32(data, 36, 1); Put32(data, 40, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(data, 60); Encoding.ASCII.GetBytes("PkRd").CopyTo(data, 64);
        Put32(data, 68, (uint)records.Count + 1); Put16(data, 76, checked((ushort)records.Count));
        for (int i = 0; i < records.Count; i++) { Put32(data, 78 + i * 8, (uint)offset); int id = i + 1; data[83 + i * 8] = (byte)(id >> 16); data[84 + i * 8] = (byte)(id >> 8); data[85 + i * 8] = (byte)id; records[i].CopyTo(data, offset); offset += records[i].Length; }
        return data;
    }
}
