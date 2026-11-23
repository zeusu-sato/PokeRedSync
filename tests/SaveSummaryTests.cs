using PokeRedSync;
using Xunit;

public class SaveSummaryTests
{
    static byte[] Part(string tag, int version, byte[] data)
    {
        byte[] b = new byte[8 + data.Length]; System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(b, 0);
        Pdb.Put16(b, 4, (ushort)version); Pdb.Put16(b, 6, (ushort)data.Length); data.CopyTo(b, 8); return b;
    }
    static byte[] Player(int version, byte[] name, bool party = true)
    {
        int tail = 14 + (version >= 2 ? 1 : 0); byte[] b = new byte[36 + tail];
        b[0] = party ? (byte)1 : (byte)0; b[1] = 25; b[35] = 42; name.CopyTo(b, b.Length - tail + 2); return Part("PLYR", version, b);
    }
    static byte[] Record(params byte[][] parts) => "PkRS"u8.ToArray().Concat(new byte[16]).Concat(parts.SelectMany(x => x)).ToArray();
    [Theory] [InlineData(1)] [InlineData(2)]
    public void ReadsPlayerAndLocation(int version)
    {
        var s = SaveSummary.Read(Record(Part("OTHR", 1, new byte[2]), Player(version, new byte[] { 1, 2, 3, 4, 5, 6 }), Part("LOCN", 1, new byte[] { 7 })));
        Assert.NotNull(s); Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, s.Name); Assert.Equal(7, s.Map); Assert.Equal(25, s.Species); Assert.Equal(42, s.Level);
    }
    [Fact] public void StopsAtTerminatorAndHandlesEmptyParty()
    {
        var s = SaveSummary.Read(Record(Player(2, new byte[] { 8, 0x50, 9 }, false), Part("LOCN", 1, new byte[] { 0 })));
        Assert.NotNull(s); Assert.Equal(new byte[] { 8 }, s.Name); Assert.Equal(0, s.Species); Assert.Equal(0, s.Level);
    }
    [Fact] public void RejectsMissingDuplicateAndMalformedParts()
    {
        byte[] player = Player(1, new byte[6]), loc = Part("LOCN", 1, new byte[] { 1 });
        Assert.Null(SaveSummary.Read(Record(player))); Assert.Null(SaveSummary.Read(Record(loc)));
        Assert.Null(SaveSummary.Read(Record(player, player, loc))); Assert.Null(SaveSummary.Read(Record(player, loc, loc)));
        Assert.Null(SaveSummary.Read(Record(Player(0, new byte[6]), loc))); Assert.Null(SaveSummary.Read(Record(player, Part("LOCN", 0, new byte[] { 1 }))));
        Assert.Null(SaveSummary.Read(Record(player, Part("LOCN", 1, Array.Empty<byte>())))); Assert.Null(SaveSummary.Read(Record(Part("PLYR", 1, new byte[49]), loc)));
        byte[] bad = Record(player, loc); Pdb.Put16(bad, 26, 65535); Assert.Null(SaveSummary.Read(bad));
        Assert.Null(SaveSummary.Read(Record(player, loc, new byte[3])));
    }
}
