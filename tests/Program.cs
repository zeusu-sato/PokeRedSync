using System.Diagnostics;
using PokeRedSync;
using Xunit;

public class SyncTests
{
[Fact]
public void SyncScenarios()
{

string root = Path.Combine(Path.GetTempPath(), "pokered-sync-test-" + Guid.NewGuid().ToString("N"));
string exe = Path.Combine(AppContext.BaseDirectory, "PokeRedSync.dll");
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
void Run() { using var p = Process.Start(new ProcessStartInfo("dotnet") { ArgumentList = { exe, "--root", root, "--once", "--log", Path.Combine(root, "log") }, UseShellExecute = false })!; p.WaitForExit(); Check(p.ExitCode == 0, "once"); }
string FileFor(string user, string folder, string name) => Path.Combine(root, user, folder, name);
byte[] Save(int kind, byte value) { var b = new byte[21]; "PkRS"u8.CopyTo(b); Pdb.Put16(b, 4, 1); Pdb.Put16(b, 6, (ushort)kind); Pdb.Put32(b, 12, 1); b[20] = value; Pdb.Put32(b, 16, Pdb.Crc(b, 16)); return b; }
byte[][] Records(uint id, uint hash, (uint, uint)[] vector, (uint, uint)[] tombs) { var sync = new byte[24 + 8 * (vector.Length + tombs.Length)]; "PkSY"u8.CopyTo(sync); Pdb.Put16(sync, 4, 1); Pdb.Put16(sync, 6, (ushort)vector.Length); Pdb.Put32(sync, 8, id); Pdb.Put32(sync, 12, hash); Pdb.Put16(sync, 16, (ushort)tombs.Length); int p = 20; foreach (var (a,b) in vector.Concat(tombs)) { Pdb.Put32(sync, p, a); Pdb.Put32(sync, p + 4, b); p += 8; } Pdb.Put32(sync, p, Pdb.Crc(sync.AsSpan(0, p))); return new[] { Save(0, (byte)id), Save(0, (byte)(id + 10)), Save(1, (byte)id), Save(1, (byte)(id + 10)), sync }; }
void Backup(string user, byte[][] recs) => File.WriteAllBytes(FileFor(user, "Backup", "PokeRedSave-PkRd.pdb"), Pdb.Write("PokeRedSave-PkRd", "save", recs, 8));
byte[][] Receive(string user) => Pdb.Read(File.ReadAllBytes(FileFor(user, "Install", "PokeRedRecv-PkRd.pdb")), "PokeRedRecv-PkRd", "recv");
void FixtureTest(string fixture)
{
 string path = Path.GetFullPath(Path.Combine("tests", "save", "sync", "fixtures", fixture));
 if (!Directory.Exists(path)) { Console.WriteLine("SKIP fixture " + (fixture == "" ? "report" : fixture)); return; }
 foreach (string u in new[] { "A", "B", "C" }) foreach (string d in new[] { "Backup", "Install" }) foreach (string f in Directory.GetFiles(Path.Combine(root, u, d))) File.Delete(f);
 using (var p = Process.Start(new ProcessStartInfo("dotnet") { ArgumentList = { exe, "--make-backup", path, FileFor("A", "Backup", "PokeRedSave-PkRd.pdb") }, UseShellExecute = false })!) { p.WaitForExit(); Check(p.ExitCode == 0, "make-backup " + fixture); }
 Run();
 byte[] pdb = File.ReadAllBytes(FileFor("B", "Install", "PokeRedRecv-PkRd.pdb"));
 var received = Pdb.Read(pdb, "PokeRedRecv-PkRd", "recv");
 Check(Pdb.U16(pdb, 32) == 0 && received.Length == 6 && received[0].AsSpan(0, 4).SequenceEqual("PkRV"u8) && Pdb.U16(received[0], 4) == 1 && Pdb.U16(received[0], 6) == 1 && Pdb.U32(received[0], 8) == Pdb.Crc(received[0].AsSpan(0, 8)) && Enumerable.Range(0, 5).All(i => received[i + 1].SequenceEqual(File.ReadAllBytes(Path.Combine(path, $"rec{(i + 4) % 5}.bin")))), "fixture bytes " + (fixture == "" ? "report" : fixture));
}
try {
 foreach (string u in new[] { "A", "B", "C" }) foreach (string d in new[] { "Backup", "Install" }) Directory.CreateDirectory(Path.Combine(root, u, d));
 FixtureTest(""); FixtureTest("anywhere");
 foreach (string u in new[] { "A", "B", "C" }) foreach (string d in new[] { "Backup", "Install" }) foreach (string f in Directory.GetFiles(Path.Combine(root, u, d))) File.Delete(f);
 var a = Records(1, 101, new[] { (1u, 1u) }, Array.Empty<(uint,uint)>()); var b = Records(2, 202, new[] { (2u, 1u) }, Array.Empty<(uint,uint)>());
 Backup("A", a); Run(); Check(!File.Exists(FileFor("A", "Install", "PokeRedRecv-PkRd.pdb")), "no self bundle");
 foreach (string u in new[] { "B", "C" }) { var r = Receive(u); Check(Pdb.U16(r[0], 6) == 1 && r[1].SequenceEqual(a[4]) && Enumerable.Range(0,4).All(i => r[i+2].SequenceEqual(a[i])), "one bundle bytes " + u); }
 string bInstall = FileFor("B", "Install", "PokeRedRecv-PkRd.pdb"), bReceived = FileFor("B", "Backup", "PokeRedRecv-PkRd.pdb");
 byte[] backedUp = File.ReadAllBytes(bInstall); Pdb.Put32(backedUp, 40, 123); Pdb.Put32(backedUp, 48, 456); File.WriteAllBytes(bReceived, backedUp);
 Run(); Check(!File.Exists(bInstall), "matching backup removes queued install despite header changes");
 backedUp[^1] ^= 1; File.WriteAllBytes(bReceived, backedUp); Run(); Check(File.Exists(bInstall), "different backup installs");
 File.Delete(bInstall); File.Delete(bReceived); Run(); Check(File.Exists(bInstall), "missing backup installs");
 string cpath = FileFor("C", "Install", "PokeRedRecv-PkRd.pdb"); DateTime before = File.GetLastWriteTimeUtc(cpath); Run(); Check(File.GetLastWriteTimeUtc(cpath) == before, "unchanged file");
 Backup("B", b); Run(); Check(Pdb.U16(Receive("C")[0], 6) == 2 && Receive("A")[1].SequenceEqual(b[4]) && Receive("B")[1].SequenceEqual(a[4]), "concurrent bundles");
 a = Records(1, 101, new[] { (1u, 1u) }, new[] { (2u, 202u) }); Backup("A", a); Run(); Check(!File.Exists(FileFor("A", "Install", "PokeRedRecv-PkRd.pdb")) && Pdb.U16(Receive("C")[0], 6) == 1 && Receive("C")[1].SequenceEqual(a[4]), "tombstone");
 Backup("A", a[..4]); Run(); Check(!File.Exists(FileFor("B", "Install", "PokeRedRecv-PkRd.pdb")) && Receive("C")[1].SequenceEqual(b[4]), "old backup and removal");
 Console.WriteLine("All tests passed.");
} finally { Directory.Delete(root, true); }

}
}
