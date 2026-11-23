using Microsoft.Win32;
using PokeRedSync;
using PokeRedSync.Update;
using System.Reflection;

internal static class Program
{
    const string SaveName = "PokeRedSave-PkRd.pdb", ReceiveName = "PokeRedRecv-PkRd.pdb";
    static string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokeRedSync", "pokered-sync.log");
    static void Log(string message) { try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(log))!); File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); } catch (IOException) { } }
    static string DefaultRoot()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
        using var key = hive.OpenSubKey(@"Software\U.S. Robotics\Pilot Desktop\Core");
        if (key?.GetValue("Path") is string path && !string.IsNullOrWhiteSpace(path)) return path;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string current = Path.Combine(local, "PiloSync"), old = Path.Combine(local, "PalmSync");
        return Path.Combine(!Directory.Exists(current) && Directory.Exists(old) ? old : current, "Palm OS Desktop");
    }
    static bool Run(string root)
    {
        try
        {
            var users = Directory.GetDirectories(root).Where(d => Directory.Exists(Path.Combine(d, "Backup"))).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
            var snapshots = new Dictionary<string, Bundle?>();
            foreach (string user in users)
            {
                string path = Path.Combine(user, "Backup", SaveName);
                if (!File.Exists(path)) { snapshots[user] = null; continue; }
                try { snapshots[user] = Bundle.Parse(Pdb.Read(File.ReadAllBytes(path), "PokeRedSave-PkRd", "save")); }
                catch (InvalidDataException) { snapshots[user] = null; }
            }
            var all = snapshots.Values.OfType<Bundle>().ToList();
            foreach (string user in users)
            {
                Bundle? own = snapshots[user];
                var selected = Delivery.Plan(all, own);
                string install = Path.Combine(user, "Install"), path = Path.Combine(install, ReceiveName);
                if (selected.Count == 0)
                {
                    if (File.Exists(path) && IsOurs(File.ReadAllBytes(path))) File.Delete(path);
                    continue;
                }
                byte[] result = Selection.Receive(selected); Pdb.Put32(result, 48, 0x50525359);
                string received = Path.Combine(user, "Backup", ReceiveName);
                if (File.Exists(received))
                {
                    try
                    {
                        var backedUp = Pdb.Read(File.ReadAllBytes(received), "PokeRedRecv-PkRd", "recv");
                        var desired = Pdb.Read(result, "PokeRedRecv-PkRd", "recv");
                        if (backedUp.Length == desired.Length && backedUp.Zip(desired).All(pair => pair.First.SequenceEqual(pair.Second)))
                        {
                            if (File.Exists(path) && IsOurs(File.ReadAllBytes(path))) File.Delete(path);
                            continue;
                        }
                    }
                    catch (InvalidDataException) { }
                }
                if (File.Exists(path)) { byte[] existing = File.ReadAllBytes(path); if (existing.SequenceEqual(result)) continue; if (!IsOurs(existing)) { Log($"Skipped unowned file: {path}"); continue; } }
                Directory.CreateDirectory(install);
                string temp = Path.Combine(install, ".pokered-sync-" + Guid.NewGuid().ToString("N") + ".tmp");
                try { File.WriteAllBytes(temp, result); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log(e.Message); return false; }
    }
    internal static bool IsOurs(byte[] b) => b.Length >= 78 && Pdb.U32(b, 48) == 0x50525359 && System.Text.Encoding.ASCII.GetString(b, 0, 32).TrimEnd('\0') == "PokeRedRecv-PkRd" && System.Text.Encoding.ASCII.GetString(b, 60, 8) == "recvPkRd";
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--help")) { Console.WriteLine("PokeRedSync [--root folder] [--log file] [--once | --show-saves]\nPokeRedSync --make-backup fixtures-folder output.pdb\nWithout --once or --show-saves, runs in the notification area."); return 0; }
            if (args.Length == 3 && args[0] == "--make-backup") { var records = Enumerable.Range(0, 5).Select(i => File.ReadAllBytes(Path.Combine(args[1], $"rec{i}.bin"))).ToArray(); File.WriteAllBytes(args[2], Pdb.Write("PokeRedSave-PkRd", "save", records, 8)); return 0; }
            string? root = null; bool once = false, showSaves = false;
            for (int i = 0; i < args.Length; i++) { if (args[i] == "--once") once = true; else if (args[i] == "--show-saves") showSaves = true; else if (args[i] == "--root" && i + 1 < args.Length) root = args[++i]; else if (args[i] == "--log" && i + 1 < args.Length) log = args[++i]; else throw new ArgumentException("Invalid arguments; use --help."); }
            root ??= DefaultRoot();
            if (showSaves) { ApplicationConfiguration.Initialize(); using var saveIcon = PalmIcon.Find(root); Application.Run(new SaveListForm(root, saveIcon ?? SystemIcons.Application)); return 0; }
            if (once) return Run(root) ? 0 : 1;
            var mutex = new TrayLock(@"Local\PokeRedSync.Tray");
            if (!mutex.TryAcquire(UpdateHandover.ReadUpdatedFrom() is not null ? TimeSpan.FromSeconds(30) : TimeSpan.Zero)) { mutex.Dispose(); Log("既に起動しているため終了します"); return 0; }
            bool ownsMutex = true;
            try
            {
            ApplicationConfiguration.Initialize();
            string? version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            string displayName = AppVersion.DisplayName(version);
            using var trayIcon = PalmIcon.Find(root);
            using var menu = new ContextMenuStrip(); using var icon = new NotifyIcon { Icon = trayIcon ?? SystemIcons.Application, Text = displayName, ContextMenuStrip = menu, Visible = true };
            menu.Items.Add(new ToolStripMenuItem(displayName) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            using var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            using var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.Size };
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\PokeRedSync");
            var settings = new UpdateSettings(key);
            string? currentTag = ReleaseSelection.CurrentTag();
            bool syncing = false, confirming = false, handingOver = false, enabled = settings.AutoUpdate;
            CancellationTokenSource? updateStop = null;
            using var shutdown = new CancellationTokenSource();
            var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            bool Process() { syncing = true; try { return Run(root); } finally { syncing = false; } }
            timer.Tick += (_, _) => { timer.Stop(); if (!Process()) timer.Start(); };
            menu.Items.Add("今すぐ処理", null, (_, _) => { timer.Stop(); if (!Process()) timer.Start(); });
            SaveListForm? saveList = null;
            menu.Items.Add("セーブの一覧", null, (_, _) =>
            {
                if (saveList is null || saveList.IsDisposed)
                {
                    saveList = new SaveListForm(root, trayIcon ?? SystemIcons.Application);
                    saveList.Show();
                }
                else { saveList.Reload(); if (saveList.WindowState == FormWindowState.Minimized) saveList.WindowState = FormWindowState.Normal; saveList.Activate(); }
            });
            var check = new ToolStripMenuItem("更新を確認");
            var auto = new ToolStripMenuItem("自動更新") { CheckOnClick = true, Checked = enabled };
            menu.Items.Add(check); menu.Items.Add(auto); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, (_, _) => Application.Exit());
            async Task<bool> TryInstall(StagedUpdate staged, CancellationToken token)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Post(async _ =>
                {
                    if (token.IsCancellationRequested || handingOver || !UpdateGate.CanInstall(menu.Visible, syncing || confirming, Application.OpenForms.Count)) { completion.TrySetResult(false); return; }
                    bool timerWasEnabled = timer.Enabled;
                    timer.Stop(); watcher.EnableRaisingEvents = false; menu.Enabled = false;
                    try { UpdateInstaller.Install(AppContext.BaseDirectory, staged.Folder); }
                    catch (Exception ex)
                    {
                        Log($"更新の適用: 失敗しました {ex.Message}");
                        watcher.EnableRaisingEvents = true; menu.Enabled = true; if (timerWasEnabled) timer.Start();
                        completion.TrySetResult(false); return;
                    }
                    handingOver = true; icon.Visible = false; mutex.Dispose(); ownsMutex = false;
                    bool result = await Task.Run(() => new UpdateHandover().Run(AppContext.BaseDirectory, staged, currentTag!, Log));
                    completion.TrySetResult(result); Application.ExitThread();
                }, null);
                return await completion.Task;
            }
            void StartUpdates()
            {
                if (!enabled || currentTag is null) return;
                updateStop?.Cancel();
                var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token); updateStop = stop;
                _ = Task.Run(async () => { try { await new UpdateScheduler(new UpdateFetcher()).RunAsync(currentTag, () => Volatile.Read(ref enabled), () => settings.FailedTag, TryInstall, Log, stop.Token); } catch (Exception ex) { Log($"更新の確認: {ex.Message}"); } });
            }
            auto.CheckedChanged += (_, _) => { enabled = auto.Checked; settings.SetAutoUpdate(enabled); if (enabled) StartUpdates(); else updateStop?.Cancel(); };
            check.Click += async (_, _) =>
            {
                check.Enabled = false;
                try
                {
                    bool Confirm(StagedUpdate staged)
                    {
                        bool answer = false;
                        context.Send(_ => { confirming = true; try { answer = MessageBox.Show($"新しい版 {staged.Version} があります。更新しますか？", "PokeRedSync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; } finally { confirming = false; } }, null);
                        return answer;
                    }
                    var result = await Task.Run(() => UpdateCheck.RunAsync(currentTag, new UpdateFetcher(), Confirm, TryInstall, Log, shutdown.Token));
                    string? message = UpdateCheck.Message(result, version);
                    if (!handingOver && message is not null) icon.ShowBalloonTip(3000, "PokeRedSync", message, ToolTipIcon.Info);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log($"更新の確認: {ex.Message}"); }
                finally { if (!handingOver) check.Enabled = true; }
            };
            void Changed(string path) { var relative = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar); if (relative.Length == 1 || (relative.Length >= 2 && relative[1].Equals("Backup", StringComparison.OrdinalIgnoreCase))) { timer.Stop(); timer.Start(); } }
            watcher.Changed += (_, e) => context.Post(_ => Changed(e.FullPath), null); watcher.Created += (_, e) => context.Post(_ => Changed(e.FullPath), null); watcher.Deleted += (_, e) => context.Post(_ => Changed(e.FullPath), null); watcher.Renamed += (_, e) => context.Post(_ => { Changed(e.OldFullPath); Changed(e.FullPath); }, null); watcher.Error += (_, _) => context.Post(_ => { timer.Stop(); timer.Start(); }, null);
            watcher.EnableRaisingEvents = true;
            try { UpdateHandover.SignalReady(); } catch (Exception ex) { Log($"更新の起動通知: {ex.Message}"); }
            try
            {
                if (UpdateHandover.ReadUpdatedFrom() is not null)
                {
                    UpdateHandover.ClearUpdatedFrom();
                    icon.ShowBalloonTip(3000, "PokeRedSync", $"PokeRedSync {version} に更新しました", ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { Log($"更新の通知: {ex.Message}"); }
            StartUpdates(); timer.Start(); Application.Run(); shutdown.Cancel(); updateStop?.Cancel(); icon.Visible = false; return 0;
            }
            finally { if (ownsMutex) { mutex.Dispose(); } }
        }
        catch (Exception e) { Log(e.ToString()); Console.Error.WriteLine(e.Message); return 1; }
    }
}


