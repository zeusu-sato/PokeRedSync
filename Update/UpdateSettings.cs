using Microsoft.Win32;

namespace PokeRedSync.Update;

public sealed class UpdateSettings(RegistryKey key)
{
    public bool AutoUpdate => key.GetValue("AutoUpdate") is not int value || value != 0;
    public void SetAutoUpdate(bool enabled) => key.SetValue("AutoUpdate", enabled ? 1 : 0, RegistryValueKind.DWord);
    public string? FailedTag => key.GetValue("UpdateFailedTag") as string;
}

public static class UpdateGate
{
    public static bool CanInstall(bool menuOpen, bool syncing, int openForms) => !menuOpen && !syncing && openForms == 0;
}
