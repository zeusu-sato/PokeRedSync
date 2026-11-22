namespace PokeRedSync;

public static class AppVersion
{
    public static string DisplayName(string? informationalVersion)
    {
        string name = "PokeRedSync " + (string.IsNullOrEmpty(informationalVersion) ? "???" : informationalVersion);
        return name.Length <= 127 ? name : name[..127];
    }
}
