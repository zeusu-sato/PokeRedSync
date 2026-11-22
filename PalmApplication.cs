namespace PokeRedSync;

public static class PalmApplication
{
    public static IEnumerable<string> Find(string root)
    {
        string[] users;
        try { users = Directory.GetDirectories(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (string user in users.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(Path.Combine(user, "Backup"))) continue;
            foreach (string folder in new[] { "Backup", "Install" })
            {
                IEnumerable<string> files;
                try { files = Directory.GetFiles(Path.Combine(user, folder)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (string file in files)
                {
                    bool match = false;
                    try
                    {
                        using var stream = File.OpenRead(file);
                        byte[] header = new byte[78];
                        match = stream.ReadAtLeast(header, header.Length, false) == header.Length &&
                            (header[33] & 1) != 0 && header.AsSpan(60, 8).SequenceEqual("applPkRd"u8);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    if (match) yield return file;
                }
            }
        }
    }
}
