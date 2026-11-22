using PokeRedSync;
using Xunit;

public class PalmApplicationTests
{
    [Fact]
    public void FindsResourceDatabaseByHeaderInUserFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            string backup = Path.Combine(root, "user", "Backup"), install = Path.Combine(root, "user", "Install");
            Directory.CreateDirectory(backup); Directory.CreateDirectory(install);
            byte[] header = new byte[78]; header[33] = 1; "applPkRd"u8.CopyTo(header.AsSpan(60));
            string match = Path.Combine(install, "unrelated-name.dat"); File.WriteAllBytes(match, header);
            header[33] = 0; File.WriteAllBytes(Path.Combine(backup, "wrong.prc"), header);
            File.WriteAllBytes(Path.Combine(backup, "short.prc"), new byte[10]);
            Assert.Equal(new[] { match }, PalmApplication.Find(root));
            Assert.Empty(PalmApplication.Find(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }
}
