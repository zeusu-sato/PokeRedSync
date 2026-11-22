namespace PokeRedSync.Update;

public static class UpdateInstaller
{
    private static readonly string[] Names = ["PokeRedSync.exe", "LICENSE"];

    public static void Install(string folder, string stagedFolder)
    {
        string previous = Path.Combine(folder, "previous");
        if (Directory.Exists(previous)) Directory.Delete(previous, true);
        Directory.CreateDirectory(previous);
        var moved = new List<string>();
        var copied = new List<string>();
        try
        {
            foreach (string name in Names)
            {
                string source = Path.Combine(folder, name);
                if (!File.Exists(source)) continue;
                File.Move(source, Path.Combine(previous, name));
                moved.Add(name);
            }
            File.WriteAllLines(Path.Combine(previous, "installed.txt"), Names);
            foreach (string name in Names)
            {
                copied.Add(name);
                File.Copy(Path.Combine(stagedFolder, name), Path.Combine(folder, name));
            }
        }
        catch
        {
            foreach (string name in copied) File.Delete(Path.Combine(folder, name));
            foreach (string name in moved) File.Move(Path.Combine(previous, name), Path.Combine(folder, name));
            throw;
        }
    }

    public static void Restore(string folder)
    {
        string previous = Path.Combine(folder, "previous");
        string manifest = Path.Combine(previous, "installed.txt");
        string[] installed = File.Exists(manifest) ? File.ReadAllLines(manifest) : [];
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                foreach (string name in Names)
                {
                    string source = Path.Combine(previous, name);
                    string destination = Path.Combine(folder, name);
                    if (installed.Contains(name, StringComparer.OrdinalIgnoreCase) || File.Exists(source)) File.Delete(destination);
                    if (File.Exists(source)) File.Copy(source, destination);
                }
                return;
            }
            catch (IOException) when (attempt < 9) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) when (attempt < 9) { Thread.Sleep(500); }
        }
    }
}
