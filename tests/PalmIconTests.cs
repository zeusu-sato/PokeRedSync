using System.Buffers.Binary;
using System.Drawing;
using PokeRedSync;
using Xunit;

public class PalmIconTests
{
    static void U16(byte[] b, int p, int v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), (ushort)v);
    static void U32(byte[] b, int p, int v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p), (uint)v);
    static byte[] BitmapData(int depth, int version, byte[] pixels, int flags = 0, int compression = 0, int density = 72, int transparent = 0, byte[]? table = null)
    {
        int header = version == 3 ? 24 : 16, width = 8 / depth;
        var b = new byte[header + (table?.Length ?? 0) + pixels.Length];
        U16(b, 0, width); U16(b, 2, 1); U16(b, 4, 1); U16(b, 6, flags); b[8] = (byte)depth; b[9] = (byte)version;
        if (version == 3) { b[10] = 24; U16(b, 14, density); U32(b, 16, transparent); } else b[12] = (byte)transparent;
        b[13] = (byte)compression; table?.CopyTo(b, header); pixels.CopyTo(b, header + (table?.Length ?? 0)); return b;
    }
    static byte[] Prc(params byte[][] images)
    {
        int length = images.Sum(a => a.Length); byte[] b = new byte[88 + length]; b[33] = 1; "applPkRd"u8.CopyTo(b.AsSpan(60)); U16(b, 76, 1); "tAIB"u8.CopyTo(b.AsSpan(78)); U16(b, 82, 1000); U32(b, 84, 88);
        int p = 88;
        for (int i = 0; i < images.Length; i++) { byte[] image = images[i]; if (i + 1 < images.Length) { if (image[9] == 3) U32(image, 20, image.Length); else U16(image, 10, image.Length / 4); } image.CopyTo(b, p); p += image.Length; }
        return b;
    }
    [Fact] public void DecodesStandardPaletteAndTransparency()
    {
        using var bitmap = PalmIcon.Decode(Prc(BitmapData(8, 2, new byte[] { 210 }, 0x2000, transparent: 210)));
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        using var color = PalmIcon.Decode(Prc(BitmapData(8, 2, new byte[] { 7 })));
        Assert.Equal(Color.FromArgb(255, 204, 204).ToArgb(), color.GetPixel(0, 0).ToArgb());
    }
    [Fact] public void UsesCustomPalette()
    {
        byte[] table = { 0, 1, 7, 12, 34, 56 };
        using var bitmap = PalmIcon.Decode(Prc(BitmapData(8, 2, new byte[] { 7 }, 0x4000, table: table)));
        Assert.Equal(Color.FromArgb(12, 34, 56).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void DecodesCompression(int type)
    {
        byte[] bytes = type switch { 0 => new byte[] { 0, 4, 128, 7 }, 1 => new byte[] { 0, 4, 1, 7 }, _ => new byte[] { 0, 4, 0, 7 } };
        using var bitmap = PalmIcon.Decode(Prc(BitmapData(8, 2, bytes, 0x8000, type)));
        Assert.Equal(Color.FromArgb(255, 204, 204).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }
    [Fact] public void PrefersHighDensityEightBitAfterDummy()
    {
        byte[] low = BitmapData(8, 2, new byte[4]); byte[] dummy = new byte[16]; dummy[8] = 255; dummy[9] = 1;
        byte[] high = BitmapData(8, 3, new byte[] { 7 }, density: 144);
        using var bitmap = PalmIcon.Decode(Prc(low, dummy, high));
        Assert.Equal(Color.FromArgb(255, 204, 204).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }
    [Theory] [InlineData(1, 0x80)] [InlineData(2, 0xc0)]
    public void DecodesPackedGrayscale(int depth, byte pixels)
    {
        using var bitmap = PalmIcon.Decode(Prc(BitmapData(depth, 1, new[] { pixels })));
        Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }
    [Theory] [InlineData(16)] [InlineData(20)] [InlineData(24)] [InlineData(32)]
    public void CropsTransparentMarginsAndUsesRequestedSize(int size)
    {
        using var source = new Bitmap(12, 10);
        for (int y = 3; y < 7; y++) for (int x = 5; x < 7; x++) source.SetPixel(x, y, Color.Red);
        using var icon = PalmIcon.MakeIcon(source, size);
        using var result = icon.ToBitmap();
        Assert.Equal(size, result.Width); Assert.Equal(size, result.Height);
        Assert.Equal(0, result.GetPixel(0, size / 2).A);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(size / 2, size / 2).ToArgb());
        Assert.NotEqual(0, result.GetPixel(size / 2, 0).A);
        Assert.NotEqual(0, result.GetPixel(size / 2, size - 1).A);
    }
    [Fact] public void EmptyBitmapStaysTransparent()
    {
        using var source = new Bitmap(4, 3);
        using var icon = PalmIcon.MakeIcon(source, 16);
        using var result = icon.ToBitmap();
        Assert.Equal(0, result.GetPixel(8, 8).A);
    }
    [Fact] public void RemovesBlueShadowBeforeCropping()
    {
        using var source = new Bitmap(10, 10);
        for (int y = 1; y < 6; y++) for (int x = 3; x < 7; x++) source.SetPixel(x, y, Color.Pink);
        for (int x = 1; x < 9; x++) source.SetPixel(x, 8, Color.Blue);
        using var icon = PalmIcon.MakeIcon(source, 20);
        using var result = icon.ToBitmap();
        Assert.Equal(Color.Pink.ToArgb(), result.GetPixel(10, 19).ToArgb());
        for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) Assert.NotEqual(Color.Blue.ToArgb(), result.GetPixel(x, y).ToArgb());
    }
    [Fact] public void KeepsOriginalWhenTooFewPixelsRemain()
    {
        using var source = new Bitmap(10, 10);
        for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++) source.SetPixel(x, y, Color.Blue);
        source.SetPixel(5, 5, Color.Pink);
        using var icon = PalmIcon.MakeIcon(source, 20);
        using var result = icon.ToBitmap();
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Pink.ToArgb(), result.GetPixel(10, 10).ToArgb());
    }
    [Fact] public void DownscalingRetainsVisiblePixels()
    {
        using var source = new Bitmap(64, 64);
        for (int y = 12; y < 52; y++) for (int x = 20; x < 44; x++) source.SetPixel(x, y, Color.Blue);
        using var icon = PalmIcon.MakeIcon(source, 16);
        using var result = icon.ToBitmap();
        Assert.True(result.GetPixel(8, 8).B > 200);
        Assert.Equal(0, result.GetPixel(0, 8).A);
    }
    [Fact] public void MissingAndCorruptFilesReturnNoIcon()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try { string backup = Path.Combine(root, "user", "Backup"); Directory.CreateDirectory(backup); Assert.Null(PalmIcon.Find(root)); byte[] b = Prc(BitmapData(8, 2, Array.Empty<byte>())); File.WriteAllBytes(Path.Combine(backup, "bad.prc"), b); Assert.Null(PalmIcon.Find(root)); }
        finally { Directory.Delete(root, true); }
    }
}
