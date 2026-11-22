using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PokeRedSync;

public static class PalmIcon
{
    static int U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p, 2));
    static int U32(byte[] b, int p) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4)));

    public static Bitmap Decode(byte[] prc)
    {
        if (prc.Length < 78 || (prc[33] & 1) == 0 || !prc.AsSpan(60, 8).SequenceEqual("applPkRd"u8)) throw new InvalidDataException();
        int count = U16(prc, 76), start = -1, end = prc.Length;
        if (78L + count * 10L > prc.Length) throw new InvalidDataException();
        for (int i = 0; i < count; i++)
        {
            int p = 78 + i * 10, offset = U32(prc, p + 6);
            if (offset < 78 + count * 10 || offset >= prc.Length) throw new InvalidDataException();
            if (prc.AsSpan(p, 4).SequenceEqual("tAIB"u8) && U16(prc, p + 4) == 1000) start = offset;
        }
        if (start < 0) throw new InvalidDataException();
        for (int i = 0; i < count; i++) { int offset = U32(prc, 84 + i * 10); if (offset > start && offset < end) end = offset; }
        byte[] data = prc[start..end];
        int pos = 0, best = -1, bestEnd = 0, bestDepth = 0, bestDensity = 0;
        while (true)
        {
            if (pos + 16 > data.Length) throw new InvalidDataException();
            int depth = data[pos + 8], version = data[pos + 9];
            if (version == 1 && depth == 255) { pos += 16; continue; }
            if (version > 3) throw new InvalidDataException();
            int density = version == 3 ? U16(data, pos + 14) : 72;
            int next = version == 3 ? U32(data, pos + 20) : U16(data, pos + 10) * 4;
            if (depth is 1 or 2 or 4 or 8 && (depth > bestDepth || depth == bestDepth && density > bestDensity)) { best = pos; bestEnd = next == 0 ? data.Length : checked(pos + next); bestDepth = depth; bestDensity = density; }
            if (next == 0) break;
            if (next < 16 || (long)pos + next >= data.Length) throw new InvalidDataException();
            pos += next;
        }
        if (best < 0) throw new InvalidDataException();
        return DecodeBitmap(data[..bestEnd], best);
    }

    static Bitmap DecodeBitmap(byte[] b, int p)
    {
        int width = U16(b, p), height = U16(b, p + 2), row = U16(b, p + 4), flags = U16(b, p + 6), depth = b[p + 8], version = b[p + 9];
        if (width == 0 || height == 0 || width > 1024 || height > 1024 || row < (width * depth + 7) / 8 || row > 4096 || (flags & 0x1600) != 0) throw new InvalidDataException();
        int size = version == 3 ? b[p + 10] : 16;
        if (size < (version == 3 ? 24 : 16) || (version == 3 && b[p + 11] != 0)) throw new InvalidDataException();
        int transparent = version == 3 ? U32(b, p + 16) : b[p + 12];
        int compression = version < 2 ? 0 : b[p + 13];
        int cursor = checked(p + size);
        Color[] palette = Palette(depth);
        if ((flags & 0x4000) != 0)
        {
            int entries = U16(b, cursor); cursor += 2;
            if (entries > 256 || cursor + entries * 4 > b.Length) throw new InvalidDataException();
            for (int i = 0; i < entries; i++, cursor += 4) { int index = b[cursor]; if (index >= palette.Length) throw new InvalidDataException(); palette[index] = Color.FromArgb(b[cursor + 1], b[cursor + 2], b[cursor + 3]); }
        }
        int limit = b.Length;
        if ((flags & 0x8000) != 0)
        {
            int length = version == 3 ? U32(b, cursor) : U16(b, cursor), prefix = version == 3 ? 4 : 2;
            if (length < prefix || (long)cursor + length > b.Length) throw new InvalidDataException();
            limit = cursor + length; cursor += prefix;
        }
        byte Read() { if (cursor >= limit) throw new InvalidDataException(); return b[cursor++]; }
        byte[] pixels = new byte[checked(row * height)];
        for (int y = 0; y < height; y++)
        {
            int target = y * row;
            if ((flags & 0x8000) == 0) { for (int x = 0; x < row; x++) pixels[target + x] = Read(); }
            else if (compression == 0)
                for (int x = 0; x < row; x += 8) { int mask = Read(); for (int k = 0; k < Math.Min(8, row - x); k++) pixels[target + x + k] = y == 0 || (mask & (0x80 >> k)) != 0 ? Read() : pixels[target + x + k - row]; }
            else if (compression == 1 || compression == 2)
            {
                int x = 0;
                while (x < row)
                {
                    int code = Read(), n;
                    if (compression == 1) { n = code; if (n == 0 || x + n > row) throw new InvalidDataException(); byte value = Read(); Array.Fill(pixels, value, target + x, n); }
                    else if (code <= 127) { n = code + 1; if (x + n > row) throw new InvalidDataException(); for (int k = 0; k < n; k++) pixels[target + x + k] = Read(); }
                    else if (code == 128) continue;
                    else { n = 257 - code; if (x + n > row) throw new InvalidDataException(); byte value = Read(); Array.Fill(pixels, value, target + x, n); }
                    x += n;
                }
            }
            else throw new InvalidDataException();
        }
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int bit = x * depth, index = (pixels[y * row + bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
            bitmap.SetPixel(x, y, (flags & 0x2000) != 0 && index == transparent ? Color.Transparent : palette[index]);
        }
        return bitmap;
    }

    static Color[] Palette(int depth)
    {
        var colors = new Color[1 << depth];
        if (depth < 8) { for (int i = 0; i < colors.Length; i++) { int v = 255 - i * 255 / (colors.Length - 1); colors[i] = Color.FromArgb(v, v, v); } return colors; }
        int n = 0;
        for (int half = 0; half < 2; half++) for (int r = 5; r >= 0; r--) for (int blue = 5 - half * 3; blue >= 3 - half * 3; blue--) for (int g = 5; g >= 0; g--) { if (n == 215) break; colors[n++] = Color.FromArgb(r * 51, g * 51, blue * 51); }
        foreach (int v in new[] { 17, 34, 68, 85, 119, 136, 170, 187, 221, 238, 192 }) colors[n++] = Color.FromArgb(v, v, v);
        foreach (var c in new[] { Color.FromArgb(128, 0, 0), Color.FromArgb(128, 0, 128), Color.FromArgb(0, 128, 0), Color.FromArgb(0, 128, 128) }) colors[n++] = c;
        while (n < 256) colors[n++] = Color.Black;
        return colors;
    }

    public static Icon? Find(string root)
    {
        foreach (string path in PalmApplication.Find(root))
        {
            try { using Bitmap bitmap = Decode(File.ReadAllBytes(path)); return MakeIcon(bitmap, SystemInformation.SmallIconSize.Width); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException or ExternalException) { }
        }
        return null;
    }

    public static Icon MakeIcon(Bitmap bitmap, int size)
    {
        using var square = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        int width = Math.Max(1, bitmap.Width * size / Math.Max(bitmap.Width, bitmap.Height)), height = Math.Max(1, bitmap.Height * size / Math.Max(bitmap.Width, bitmap.Height));
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) square.SetPixel((size - width) / 2 + x, (size - height) / 2 + y, bitmap.GetPixel(x * bitmap.Width / width, y * bitmap.Height / height));
        IntPtr handle = square.GetHicon();
        try { using Icon temp = Icon.FromHandle(handle); return (Icon)temp.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
