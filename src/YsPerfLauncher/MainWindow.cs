using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace YsPerfLauncher;

public class MainWindow : Window
{
    const string GameUrl = "https://yanderesimulator.com/dl/latest.zip";
    const string DiscordUrl = "https://discord.gg/Qf5cgQK3Y2";
    const string YoutubeUrl = "https://www.youtube.com/@xyzhakuu";
    const string GithubUrl = "https://github.com/hakuxyz/YandereSimPerfPatch";

    static readonly Color Bg = Color.Parse("#0A0A0B");
    static readonly Color Fg = Color.Parse("#E6E6E9");
    static readonly Color Muted = Color.Parse("#8A8A93");
    static readonly Color CtrlBg = Color.Parse("#141417");
    static readonly Color CtrlBorder = Color.Parse("#26262B");
    static readonly Color Green = Color.Parse("#6FCF97");
    static readonly Color Red = Color.Parse("#E06C75");

    readonly TextBox _folderBox = new()
    {
        Watermark = "Folder that contains YandereSimulator.exe",
        Background = new SolidColorBrush(CtrlBg),
        BorderBrush = new SolidColorBrush(CtrlBorder),
        Foreground = new SolidColorBrush(Fg),
        SelectionBrush = new SolidColorBrush(CtrlBorder),
        Height = 32,
        FontSize = 13,
        Padding = new Thickness(8, 4),
    };

    readonly Ellipse _statusDot = new() { Width = 8, Height = 8, Fill = new SolidColorBrush(Muted) };
    readonly TextBlock _statusText = new() { Text = "No game folder set", Foreground = new SolidColorBrush(Muted), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    readonly Button _btnPatch;
    readonly Button _btnRestore;
    readonly Button _btnLaunch;
    readonly Button _btnDownload;

    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };

    readonly TextBox _logBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Background = new SolidColorBrush(Bg),
        BorderBrush = new SolidColorBrush(CtrlBorder),
        Foreground = new SolidColorBrush(Fg),
        FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,monospace"),
        FontSize = 12,
        Text = "",
    };

    bool _busy;
    string _gameDir = "";

    public MainWindow()
    {
        Title = "YandereSim Perf Patcher";
        Width = 720;
        Height = 520;
        Background = new SolidColorBrush(Bg);

        _btnPatch = MakeButton("Patch", OnPatchClicked, primary: true);
        _btnRestore = MakeButton("Restore", OnRestoreClicked);
        _btnLaunch = MakeButton("Launch Game", OnLaunchClicked);
        _btnDownload = MakeButton("Download Game", OnDownloadClicked);

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(_progress, Dock.Bottom);
        root.Children.Add(_progress);

        var logBorder = new Border
        {
            Child = _logBox,
            BorderBrush = new SolidColorBrush(CtrlBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        DockPanel.SetDock(logBorder, Dock.Bottom);
        root.Children.Add(logBorder);

        var main = new StackPanel { Spacing = 12, Margin = new Thickness(0, 4, 0, 8) };
        main.Children.Add(BuildFolderRow());
        main.Children.Add(BuildStatusRow());
        main.Children.Add(BuildActionsRow());
        root.Children.Add(main);

        Content = root;

        LoadConfig();
        _folderBox.Text = _gameDir;
        _folderBox.TextChanged += (_, _) => { _gameDir = _folderBox.Text?.Trim() ?? ""; RefreshStatus(); SaveConfig(); };

        RefreshStatus();

        Closed += (_, _) => SaveConfig();
    }

    Border BuildHeader()
    {
        var title = new TextBlock
        {
            Text = "YandereSim Perf Patcher",
            Foreground = new SolidColorBrush(Fg),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        links.Children.Add(MakeLink("Discord", DiscordUrl));
        links.Children.Add(MakeLink("YouTube", YoutubeUrl));
        links.Children.Add(MakeLink("GitHub", GithubUrl));

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(title, Dock.Left);
        bar.Children.Add(title);
        bar.Children.Add(links);
        return new Border
        {
            Child = bar,
            BorderBrush = new SolidColorBrush(CtrlBorder),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 10),
        };
    }

    Grid BuildFolderRow()
    {
        var browse = MakeButton("Browse...", OnBrowseClicked);
        browse.Width = 90;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_folderBox, 0);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(_folderBox);
        grid.Children.Add(browse);
        return grid;
    }

    StackPanel BuildStatusRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_statusDot);
        row.Children.Add(_statusText);
        return row;
    }

    StackPanel BuildActionsRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(_btnPatch);
        row.Children.Add(_btnRestore);
        row.Children.Add(_btnLaunch);
        row.Children.Add(_btnDownload);
        return row;
    }

    static Button MakeButton(string text, EventHandler<RoutedEventArgs> onClick, bool primary = false)
    {
        var btn = new Button
        {
            Content = text,
            Background = new SolidColorBrush(CtrlBg),
            BorderBrush = new SolidColorBrush(primary ? Fg : CtrlBorder),
            Foreground = new SolidColorBrush(Fg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 6),
            FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        btn.Click += onClick;
        btn.PointerEntered += (_, _) => btn.BorderBrush = new SolidColorBrush(primary ? Fg : Muted);
        btn.PointerExited += (_, _) => btn.BorderBrush = new SolidColorBrush(primary ? Fg : CtrlBorder);
        return btn;
    }

    static Button MakeLink(string text, string url)
    {
        var btn = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Muted),
            Padding = new Thickness(0),
            FontSize = 13,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        btn.PointerEntered += (_, _) => btn.Foreground = new SolidColorBrush(Fg);
        btn.PointerExited += (_, _) => btn.Foreground = new SolidColorBrush(Muted);
        btn.Click += (_, _) => OpenUrl(url);
        return btn;
    }

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* browser will open if registered */ }
    }

    async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Yandere Simulator folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return;
        _gameDir = path;
        _folderBox.Text = path;
        RefreshStatus();
        SaveConfig();
    }

    void RefreshStatus()
    {
        var state = GetState();
        switch (state)
        {
            case GameState.NoFolder:
                _statusDot.Fill = new SolidColorBrush(Muted);
                _statusText.Text = "No game folder set";
                break;
            case GameState.NotFound:
                _statusDot.Fill = new SolidColorBrush(Red);
                _statusText.Text = "Game not found in this folder";
                break;
            case GameState.Vanilla:
                _statusDot.Fill = new SolidColorBrush(Muted);
                _statusText.Text = "Vanilla (unpatched)";
                break;
            case GameState.Patched:
                _statusDot.Fill = new SolidColorBrush(Green);
                _statusText.Text = "Patched";
                break;
        }

        bool hasGame = state is GameState.Vanilla or GameState.Patched;
        _btnPatch.IsEnabled = !_busy && hasGame;
        _btnRestore.IsEnabled = !_busy && hasGame;
        _btnLaunch.IsEnabled = !_busy && hasGame;
        _btnDownload.IsEnabled = !_busy;
    }

    GameState GetState()
    {
        if (string.IsNullOrWhiteSpace(_gameDir)) return GameState.NoFolder;
        var dll = DllPath();
        if (dll == null || !File.Exists(dll)) return GameState.NotFound;
        return IsPatched(dll) ? GameState.Patched : GameState.Vanilla;
    }

    string? DllPath()
    {
        if (string.IsNullOrWhiteSpace(_gameDir)) return null;
        return Path.Combine(_gameDir, "YandereSimulator_Data", "Managed", "Assembly-CSharp.dll");
    }

    static bool IsPatched(string dll)
    {
        var bytes = File.ReadAllBytes(dll);
        var needle = Encoding.ASCII.GetBytes("F10NotifTimer");
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    async void OnPatchClicked(object? sender, RoutedEventArgs e)
    {
        var dll = DllPath();
        if (dll == null || !File.Exists(dll)) { Log("Assembly-CSharp.dll not found."); return; }

        SetBusy(true);
        Log("Patching...");
        try
        {
            await Task.Run(() =>
            {
                var orig = dll + ".orig";
                if (File.Exists(orig))
                {
                    File.Copy(orig, dll, overwrite: true);
                    Dispatcher.UIThread.Post(() => Log("Restored original DLL before patching."));
                }
                else if (IsPatched(dll))
                {
                    Dispatcher.UIThread.Post(() => Log("Already patched and no .orig backup found. Nothing to do."));
                    return;
                }

                string output;
                int rc;
                var sw = new StringWriter();
                var oldOut = Console.Out;
                var oldErr = Console.Error;
                Console.SetOut(sw);
                Console.SetError(sw);
                try
                {
                    rc = new Patcher(dll, Path.GetDirectoryName(dll)!).Run();
                }
                catch (Exception ex)
                {
                    rc = 3;
                    sw.WriteLine($"PATCH ABORTED (file untouched): {ex.Message}");
                }
                finally
                {
                    Console.SetOut(oldOut);
                    Console.SetError(oldErr);
                }
                output = sw.ToString();
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var line in output.Split('\n'))
                    {
                        var trimmed = line.TrimEnd('\r');
                        if (trimmed.Length > 0) Log(trimmed);
                    }
                    switch (rc)
                    {
                        case 0: Log("Patched."); break;
                        case 2: Log("Already patched, nothing to do."); break;
                        default: Log($"Patcher stopped (code {rc})."); break;
                    }
                });
            });
        }
        catch (Exception ex)
        {
            Log($"Patch failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            RefreshStatus();
        }
    }

    async void OnRestoreClicked(object? sender, RoutedEventArgs e)
    {
        var dll = DllPath();
        if (dll == null || !File.Exists(dll)) { Log("Assembly-CSharp.dll not found."); return; }
        var orig = dll + ".orig";
        if (!File.Exists(orig)) { Log($"No backup found at {orig}."); return; }

        try
        {
            await Task.Run(() => File.Copy(orig, dll, overwrite: true));
            Log("Restored the original Assembly-CSharp.dll.");
        }
        catch (Exception ex)
        {
            Log($"Restore failed: {ex.Message}");
        }
        RefreshStatus();
    }

    void OnLaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameDir)) { Log("Set the game folder first."); return; }

        var bashScript = Path.Combine(_gameDir, "start-mangohud.sh");
        if (File.Exists(bashScript)) { LaunchScript(bashScript); return; }

        var shScript = Path.Combine(_gameDir, "start.sh");
        if (File.Exists(shScript)) { LaunchScript(shScript); return; }

        var native = Path.Combine(_gameDir, "YandereSimulator.x86_64");
        if (File.Exists(native)) { LaunchNative(native); return; }

        var exe = Path.Combine(_gameDir, "YandereSimulator.exe");
        if (!File.Exists(exe)) { Log("No YandereSimulator executable found."); return; }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = _gameDir });
                Log("Starting game...");
            }
            catch (Exception ex) { Log($"Could not start game: {ex.Message}"); }
            return;
        }

        var wine = FindExecutable("wine");
        if (wine == null) { Log("No start script, no native binary and no wine found."); return; }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = wine,
                WorkingDirectory = _gameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(exe);
            Process.Start(psi);
            Log("Starting game via wine...");
        }
        catch (Exception ex) { Log($"Could not start game: {ex.Message}"); }
    }

    void LaunchScript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                WorkingDirectory = _gameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(script);
            Process.Start(psi);
            Log($"Starting game via {Path.GetFileName(script)}...");
        }
        catch (Exception ex) { Log($"Could not start game: {ex.Message}"); }
    }

    void LaunchNative(string binary)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = binary,
                WorkingDirectory = _gameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Log("Starting game (native build)...");
        }
        catch (Exception ex) { Log($"Could not start game: {ex.Message}"); }
    }

    static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    async void OnDownloadClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameDir))
        {
            Log("Choose or create the game folder first, then press Download again.");
            return;
        }

        try { Directory.CreateDirectory(_gameDir); }
        catch (Exception ex) { Log($"Cannot create folder: {ex.Message}"); return; }

        SetBusy(true);
        _progress.IsVisible = true;
        _progress.Value = 0;
        Log($"Downloading {GameUrl} ...");

        var zipPath = Path.Combine(_gameDir, "latest.zip");
        var client = new HttpClient();
        client.Timeout = Timeout.InfiniteTimeSpan;

        try
        {
            using var resp = await client.GetAsync(GameUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            await using var input = await resp.Content.ReadAsStreamAsync();
            await using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);

            var buffer = new byte[1 << 20];
            long read = 0;
            int n;
            var lastPct = -1;
            while ((n = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await output.WriteAsync(buffer, 0, n);
                read += n;
                if (total > 0)
                {
                    var pct = (int)(read * 100 / total);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        var r = read;
                        var t = total;
                        Dispatcher.UIThread.Post(() =>
                        {
                            _progress.Value = pct;
                        });
                    }
                }
            }

            _progress.IsIndeterminate = true;
            Log("Download finished, extracting...");
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, _gameDir, overwriteFiles: true));
            _progress.IsIndeterminate = false;
            _progress.Value = 100;

            try { File.Delete(zipPath); } catch { /* archive removed on best effort */ }

            var exe = Path.Combine(_gameDir, "YandereSimulator.exe");
            if (!File.Exists(exe))
            {
                foreach (var dir in Directory.GetDirectories(_gameDir))
                {
                    var sub = Path.Combine(dir, "YandereSimulator.exe");
                    if (File.Exists(sub))
                    {
                        _gameDir = dir;
                        _folderBox.Text = dir;
                        Log($"Game files found in {dir}");
                        break;
                    }
                }
            }
            if (File.Exists(Path.Combine(_gameDir, "YandereSimulator.exe")))
                Log("Download and extraction complete. You can patch and launch now.");
            else
                Log("Extraction done, but no YandereSimulator.exe found. Check the folder.");
            SaveConfig();
        }
        catch (Exception ex)
        {
            Log($"Download failed: {ex.Message}");
            _progress.IsIndeterminate = false;
        }
        finally
        {
            client.Dispose();
            _progress.IsVisible = false;
            _progress.IsIndeterminate = false;
            SetBusy(false);
            RefreshStatus();
        }
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        _folderBox.IsEnabled = !busy;
        RefreshStatus();
    }

    void Log(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logBox.Text += line + "\n";
            _logBox.CaretIndex = _logBox.Text.Length;
        });
    }

    string ConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YsPerfLauncher");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath())) return;
            var json = File.ReadAllText(ConfigPath());
            var cfg = JsonSerializer.Deserialize<Config>(json);
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.GameDir))
                _gameDir = cfg.GameDir;
        }
        catch { /* corrupted config is fine */ }
    }

    void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(new Config { GameDir = _gameDir });
            File.WriteAllText(ConfigPath(), json);
        }
        catch { /* best effort */ }
    }

    enum GameState { NoFolder, NotFound, Vanilla, Patched }

    class Config
    {
        public string GameDir { get; set; } = "";
    }
}
