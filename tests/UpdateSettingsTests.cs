using Microsoft.Win32;
using PokeRedSync.Update;
using Xunit;

public class UpdateSettingsTests
{
    [Fact]
    public void DefaultsToEnabledAndPersistsChanges()
    {
        string path = @"Software\PokeRedSync-Test-" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            var settings = new UpdateSettings(key);
            Assert.True(settings.AutoUpdate);
            settings.SetAutoUpdate(false);
            Assert.False(new UpdateSettings(key).AutoUpdate);
            Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("AutoUpdate"));
            settings.SetAutoUpdate(true);
            Assert.True(settings.AutoUpdate);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    [Theory]
    [InlineData(false, false, 0, true)]
    [InlineData(true, false, 0, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, false, 1, false)]
    public void Gate(bool menu, bool syncing, int forms, bool expected) =>
        Assert.Equal(expected, UpdateGate.CanInstall(menu, syncing, forms));
}
