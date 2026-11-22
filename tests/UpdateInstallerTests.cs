using PokeRedSync.Update;
using Xunit;

public class UpdateInstallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstallAndRestore(bool fail)
    {
        string root = Path.Combine(Path.GetTempPath(), "PokeRedSync-test-" + Guid.NewGuid().ToString("N"));
        string staged = Path.Combine(root, "staged");
        Directory.CreateDirectory(staged);
        try
        {
            File.WriteAllText(Path.Combine(root, "PokeRedSync.exe"), "old");
            File.WriteAllText(Path.Combine(root, "unrelated"), "keep");
            File.WriteAllText(Path.Combine(staged, "PokeRedSync.exe"), "new");
            if (!fail) File.WriteAllText(Path.Combine(staged, "LICENSE"), "new license");
            if (fail) Assert.Throws<FileNotFoundException>(() => UpdateInstaller.Install(root, staged));
            else
            {
                UpdateInstaller.Install(root, staged);
                Assert.Equal("new", File.ReadAllText(Path.Combine(root, "PokeRedSync.exe")));
                UpdateInstaller.Restore(root);
                Assert.False(File.Exists(Path.Combine(root, "LICENSE")));
            }
            Assert.Equal("old", File.ReadAllText(Path.Combine(root, "PokeRedSync.exe")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "unrelated")));
        }
        finally { Directory.Delete(root, true); }
    }
}
