using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AfterTheFallVRModKit.Manager
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppId = "751630";
        private const string PluginGuid = "local.afterthefall.vrmodkit";
        private const string PluginFileName = "AfterTheFallVRModKit.dll";
        private const string PluginDisabledFileName = "AfterTheFallVRModKit.dll.disabled";
        private const string EmbeddedPayloadResourceName = "AfterTheFallVRModKit.Payload.zip";

        private readonly TextBox gamePathText;
        private readonly Label statusLabel;
        private readonly Label adminLabel;
        private readonly CheckBox bepInExCheck;
        private readonly CheckBox pluginCheck;
        private readonly CheckBox voipCheck;
        private readonly CheckBox bloodCheck;
        private readonly CheckBox serverCleanupCheck;
        private readonly CheckBox vrPerfKitCheck;
        private readonly TextBox logText;
        private readonly Button applyButton;
        private readonly Button refreshButton;
        private readonly Button browseButton;
        private readonly Button openFolderButton;
        private readonly Button launchButton;
        private readonly Button adminButton;
        private static string payloadDir;

        public MainForm()
        {
            Text = "After The Fall VR Mod Kit";
            Width = 760;
            Height = 680;
            MinimumSize = new Size(700, 620);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(16);
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var title = new Label();
            title.Text = "After The Fall VR Mod Kit";
            title.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
            title.AutoSize = true;
            root.Controls.Add(title);

            var pathPanel = new TableLayoutPanel();
            pathPanel.Dock = DockStyle.Top;
            pathPanel.ColumnCount = 3;
            pathPanel.RowCount = 2;
            pathPanel.Margin = new Padding(0, 14, 0, 8);
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pathPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var pathLabel = new Label();
            pathLabel.Text = "Game folder";
            pathLabel.AutoSize = true;
            pathLabel.Margin = new Padding(0, 0, 0, 4);
            pathPanel.Controls.Add(pathLabel, 0, 0);

            gamePathText = new TextBox();
            gamePathText.Dock = DockStyle.Fill;
            gamePathText.Margin = new Padding(0, 0, 8, 0);
            pathPanel.Controls.Add(gamePathText, 0, 1);

            browseButton = new Button();
            browseButton.Text = "Browse";
            browseButton.AutoSize = true;
            browseButton.Margin = new Padding(0, 0, 8, 0);
            browseButton.Click += delegate { BrowseForGameFolder(); };
            pathPanel.Controls.Add(browseButton, 1, 1);

            refreshButton = new Button();
            refreshButton.Text = "Refresh";
            refreshButton.AutoSize = true;
            refreshButton.Click += delegate { RefreshStatus(); };
            pathPanel.Controls.Add(refreshButton, 2, 1);
            root.Controls.Add(pathPanel);

            var topStatusPanel = new TableLayoutPanel();
            topStatusPanel.Dock = DockStyle.Top;
            topStatusPanel.ColumnCount = 2;
            topStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.ForeColor = Color.DimGray;
            statusLabel.Margin = new Padding(0, 2, 0, 8);
            topStatusPanel.Controls.Add(statusLabel, 0, 0);

            adminLabel = new Label();
            adminLabel.AutoSize = true;
            adminLabel.Margin = new Padding(0, 2, 0, 8);
            topStatusPanel.Controls.Add(adminLabel, 1, 0);
            root.Controls.Add(topStatusPanel);

            var toggles = new GroupBox();
            toggles.Text = "Toggles";
            toggles.Dock = DockStyle.Fill;
            toggles.Padding = new Padding(12);

            var toggleLayout = new TableLayoutPanel();
            toggleLayout.Dock = DockStyle.Fill;
            toggleLayout.ColumnCount = 1;
            toggleLayout.RowCount = 6;
            for (var i = 0; i < 6; i++)
            {
                toggleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            toggles.Controls.Add(toggleLayout);

            bepInExCheck = MakeCheckBox("BepInEx mod loader", "Installs or enables the IL2CPP mod loader used by the patch.");
            pluginCheck = MakeCheckBox("After The Fall VR Mod Kit", "Installs or enables the bundled VR mod kit plugin.");
            voipCheck = MakeCheckBox("Disable in-game VOIP", "Skips the game's built-in VOIP handlers. Use external voice chat instead.");
            bloodCheck = MakeCheckBox("Suppress client blood/gore visuals", "Skips client-side blood, decal, gib, and mutilation visual handlers.");
            serverCleanupCheck = MakeCheckBox("Clean retained ServerGame on hub return", "Experimental cleanup for the ServerGame memory leak seen after horde.");
            vrPerfKitCheck = MakeCheckBox("vrperfkit injection", "Enables dxgi.dll based vrperfkit injection when the package or an existing install is present.");

            toggleLayout.Controls.Add(bepInExCheck);
            toggleLayout.Controls.Add(pluginCheck);
            toggleLayout.Controls.Add(voipCheck);
            toggleLayout.Controls.Add(bloodCheck);
            toggleLayout.Controls.Add(serverCleanupCheck);
            toggleLayout.Controls.Add(vrPerfKitCheck);
            root.Controls.Add(toggles);

            var actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Top;
            actionPanel.FlowDirection = FlowDirection.LeftToRight;
            actionPanel.WrapContents = true;
            actionPanel.Margin = new Padding(0, 12, 0, 8);

            applyButton = new Button();
            applyButton.Text = "Apply";
            applyButton.Width = 120;
            applyButton.Height = 32;
            applyButton.Click += delegate { ApplyChanges(); };
            actionPanel.Controls.Add(applyButton);

            openFolderButton = new Button();
            openFolderButton.Text = "Open Game Folder";
            openFolderButton.AutoSize = true;
            openFolderButton.Height = 32;
            openFolderButton.Click += delegate { OpenGameFolder(); };
            actionPanel.Controls.Add(openFolderButton);

            launchButton = new Button();
            launchButton.Text = "Launch Game";
            launchButton.AutoSize = true;
            launchButton.Height = 32;
            launchButton.Click += delegate { LaunchGame(); };
            actionPanel.Controls.Add(launchButton);

            adminButton = new Button();
            adminButton.Text = "Restart as Admin";
            adminButton.AutoSize = true;
            adminButton.Height = 32;
            adminButton.Click += delegate { RestartAsAdmin(); };
            actionPanel.Controls.Add(adminButton);
            root.Controls.Add(actionPanel);

            logText = new TextBox();
            logText.Dock = DockStyle.Fill;
            logText.Multiline = true;
            logText.ReadOnly = true;
            logText.ScrollBars = ScrollBars.Vertical;
            logText.Font = new Font("Consolas", 9);
            root.Controls.Add(logText);

            var footer = new Label();
            footer.AutoSize = true;
            footer.ForeColor = Color.DimGray;
            footer.Text = "Close After The Fall before applying file changes. Config changes take effect on the next game launch.";
            footer.Margin = new Padding(0, 8, 0, 0);
            root.Controls.Add(footer);

            gamePathText.Text = DetectGameRoot();
            UpdateAdminStatus();
            RefreshStatus();
        }

        private static CheckBox MakeCheckBox(string text, string tooltip)
        {
            var checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.AutoSize = true;
            checkBox.Margin = new Padding(0, 6, 0, 6);
            checkBox.Tag = tooltip;
            new ToolTip().SetToolTip(checkBox, tooltip);
            return checkBox;
        }

        private void RefreshStatus()
        {
            try
            {
                var gameRoot = GetGameRootFromText();
                var valid = IsGameRoot(gameRoot);
                statusLabel.Text = valid ? "Detected After The Fall." : "Game folder not detected. Choose the folder that contains AfterTheFall.exe.";

                if (!valid)
                {
                    Log("Game folder is not valid: " + gameRoot);
                    return;
                }

                bepInExCheck.Checked = File.Exists(Path.Combine(gameRoot, "winhttp.dll"));
                pluginCheck.Checked = File.Exists(PluginPath(gameRoot));
                vrPerfKitCheck.Checked = File.Exists(Path.Combine(gameRoot, "dxgi.dll"));

                var config = ReadFeatureConfig(gameRoot);
                voipCheck.Checked = config.DisableInGameVoip;
                bloodCheck.Checked = config.SuppressClientBloodAndGore;
                serverCleanupCheck.Checked = config.CleanupRetainedServerGame;

                Log("Status refreshed for " + gameRoot);
                Log("BepInEx: " + StateFromFiles(Path.Combine(gameRoot, "winhttp.dll"), Path.Combine(gameRoot, "winhttp.dll.disabled")));
                Log("VR mod kit: " + StateFromFiles(PluginPath(gameRoot), PluginDisabledPath(gameRoot)));
                Log("vrperfkit: " + StateFromFiles(Path.Combine(gameRoot, "dxgi.dll"), Path.Combine(gameRoot, "dxgi.dll.disabled")));
            }
            catch (Exception ex)
            {
                Log("Refresh failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyChanges()
        {
            try
            {
                var gameRoot = GetGameRootFromText();
                if (!IsGameRoot(gameRoot))
                {
                    throw new InvalidOperationException("That folder does not contain AfterTheFall.exe.");
                }

                if (IsGameRunning())
                {
                    throw new InvalidOperationException("After The Fall is running. Close the game before applying file changes.");
                }

                ApplyBepInEx(gameRoot, bepInExCheck.Checked);
                ApplyPlugin(gameRoot, pluginCheck.Checked);
                WriteFeatureConfig(gameRoot, new FeatureConfig
                {
                    DisableInGameVoip = voipCheck.Checked,
                    SuppressClientBloodAndGore = bloodCheck.Checked,
                    CleanupRetainedServerGame = serverCleanupCheck.Checked
                });
                ApplyVrPerfKit(gameRoot, vrPerfKitCheck.Checked);

                Log("Apply complete.");
                RefreshStatus();
                MessageBox.Show(this, "Changes applied. Launch the game when ready.", "After The Fall VR Mod Kit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Apply failed: " + ex.Message);
                MessageBox.Show(this, "Windows blocked write access. Restart this manager as Administrator, then apply again.", "Permission needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log("Apply failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyBepInEx(string gameRoot, bool enabled)
        {
            var active = Path.Combine(gameRoot, "winhttp.dll");
            var disabled = Path.Combine(gameRoot, "winhttp.dll.disabled");

            if (enabled)
            {
                if (!File.Exists(active))
                {
                    if (File.Exists(disabled))
                    {
                        MoveFile(disabled, active);
                        Log("Enabled BepInEx loader.");
                    }
                    else
                    {
                        var payload = Path.Combine(PayloadDir(), "bepinex");
                        if (!Directory.Exists(payload))
                        {
                            throw new FileNotFoundException("BepInEx payload is missing from the package.", payload);
                        }

                        CopyDirectory(payload, gameRoot);
                        Log("Installed BepInEx loader.");
                    }
                }
                else
                {
                    Log("BepInEx loader already enabled.");
                }
            }
            else
            {
                if (File.Exists(active))
                {
                    MoveFile(active, disabled);
                    Log("Disabled BepInEx loader.");
                }
                else
                {
                    Log("BepInEx loader already disabled or not installed.");
                }
            }
        }

        private void ApplyPlugin(string gameRoot, bool enabled)
        {
            var active = PluginPath(gameRoot);
            var disabled = PluginDisabledPath(gameRoot);
            var payload = Path.Combine(PayloadDir(), PluginFileName);

            if (enabled)
            {
                if (!File.Exists(payload))
                {
                    throw new FileNotFoundException("Plugin payload is missing from the package.", payload);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(active));
                File.Copy(payload, active, true);
                if (File.Exists(disabled))
                {
                    File.Delete(disabled);
                }

                Log("Installed/enabled After The Fall VR Mod Kit.");
            }
            else
            {
                if (File.Exists(active))
                {
                    MoveFile(active, disabled);
                    Log("Disabled After The Fall VR Mod Kit.");
                }
                else
                {
                    Log("VR mod kit already disabled or not installed.");
                }
            }
        }

        private void ApplyVrPerfKit(string gameRoot, bool enabled)
        {
            var active = Path.Combine(gameRoot, "dxgi.dll");
            var disabled = Path.Combine(gameRoot, "dxgi.dll.disabled");
            var config = Path.Combine(gameRoot, "vrperfkit.yml");
            var payload = Path.Combine(PayloadDir(), "vrperfkit");
            var payloadDll = Path.Combine(payload, "dxgi.dll");
            var payloadConfig = Path.Combine(payload, "vrperfkit.yml");

            if (enabled)
            {
                if (!File.Exists(active))
                {
                    if (File.Exists(disabled))
                    {
                        MoveFile(disabled, active);
                        Log("Enabled vrperfkit injection.");
                    }
                    else if (File.Exists(payloadDll))
                    {
                        File.Copy(payloadDll, active, true);
                        Log("Installed vrperfkit dxgi.dll.");
                    }
                    else
                    {
                        throw new FileNotFoundException("vrperfkit is not installed and no vrperfkit payload was bundled.", payloadDll);
                    }
                }
                else
                {
                    Log("vrperfkit injection already enabled.");
                }

                if (!File.Exists(config) && File.Exists(payloadConfig))
                {
                    File.Copy(payloadConfig, config, false);
                    Log("Installed vrperfkit.yml.");
                }
            }
            else
            {
                if (File.Exists(active))
                {
                    MoveFile(active, disabled);
                    Log("Disabled vrperfkit injection.");
                }
                else
                {
                    Log("vrperfkit injection already disabled or not installed.");
                }
            }
        }

        private FeatureConfig ReadFeatureConfig(string gameRoot)
        {
            var result = FeatureConfig.Defaults();
            var path = ConfigPath(gameRoot);
            if (!File.Exists(path))
            {
                return result;
            }

            var inFeatures = false;
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                {
                    continue;
                }

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inFeatures = string.Equals(trimmed, "[Features]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inFeatures)
                {
                    continue;
                }

                var equals = trimmed.IndexOf('=');
                if (equals < 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, equals).Trim();
                var value = trimmed.Substring(equals + 1).Trim();
                bool parsed;
                if (!bool.TryParse(value, out parsed))
                {
                    continue;
                }

                if (string.Equals(key, "DisableInGameVoip", StringComparison.OrdinalIgnoreCase))
                {
                    result.DisableInGameVoip = parsed;
                }
                else if (string.Equals(key, "SuppressClientBloodAndGore", StringComparison.OrdinalIgnoreCase))
                {
                    result.SuppressClientBloodAndGore = parsed;
                }
                else if (string.Equals(key, "CleanupRetainedServerGame", StringComparison.OrdinalIgnoreCase))
                {
                    result.CleanupRetainedServerGame = parsed;
                }
            }

            return result;
        }

        private void WriteFeatureConfig(string gameRoot, FeatureConfig config)
        {
            var path = ConfigPath(gameRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var builder = new StringBuilder();
            builder.AppendLine("## Settings file was generated by After The Fall VR Mod Kit.");
            builder.AppendLine("## Plugin GUID: " + PluginGuid);
            builder.AppendLine();
            builder.AppendLine("[Features]");
            builder.AppendLine();
            builder.AppendLine("## Disable After The Fall's built-in VOIP handlers. Leave this on when using Discord or another voice chat.");
            builder.AppendLine("DisableInGameVoip = " + config.DisableInGameVoip.ToString().ToLowerInvariant());
            builder.AppendLine();
            builder.AppendLine("## Skip client-side blood, decal, gib, and mutilation visual handlers.");
            builder.AppendLine("SuppressClientBloodAndGore = " + config.SuppressClientBloodAndGore.ToString().ToLowerInvariant());
            builder.AppendLine();
            builder.AppendLine("## After returning to the hub, dispose a retained ServerGame instance if the game leaves one in memory.");
            builder.AppendLine("CleanupRetainedServerGame = " + config.CleanupRetainedServerGame.ToString().ToLowerInvariant());

            File.WriteAllText(path, builder.ToString());
            Log("Wrote plugin config: " + path);
        }

        private void BrowseForGameFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the After The Fall game folder";
                dialog.SelectedPath = Directory.Exists(gamePathText.Text) ? gamePathText.Text : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    gamePathText.Text = dialog.SelectedPath;
                    RefreshStatus();
                }
            }
        }

        private void OpenGameFolder()
        {
            var gameRoot = GetGameRootFromText();
            if (Directory.Exists(gameRoot))
            {
                Process.Start(gameRoot);
            }
        }

        private void LaunchGame()
        {
            Process.Start("steam://rungameid/" + AppId);
        }

        private void RestartAsAdmin()
        {
            try
            {
                var exe = Application.ExecutablePath;
                var start = new ProcessStartInfo(exe);
                start.UseShellExecute = true;
                start.Verb = "runas";
                Process.Start(start);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not restart as Administrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateAdminStatus()
        {
            var admin = IsAdministrator();
            adminLabel.Text = admin ? "Administrator" : "Standard user";
            adminLabel.ForeColor = admin ? Color.DarkGreen : Color.DarkOrange;
            adminButton.Visible = !admin;
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool IsGameRunning()
        {
            return Process.GetProcessesByName("AfterTheFall").Length > 0;
        }

        private static bool IsGameRoot(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "AfterTheFall.exe"));
        }

        private string GetGameRootFromText()
        {
            return (gamePathText.Text ?? string.Empty).Trim().Trim('"');
        }

        private static string DetectGameRoot()
        {
            var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "After The Fall");
            if (IsGameRoot(common))
            {
                return common;
            }

            foreach (var steamApps in GetSteamAppsFolders())
            {
                var manifest = Path.Combine(steamApps, "appmanifest_" + AppId + ".acf");
                if (!File.Exists(manifest))
                {
                    continue;
                }

                var installDir = ReadAcfValue(manifest, "installdir");
                if (installDir.Length == 0)
                {
                    installDir = "After The Fall";
                }

                var root = Path.Combine(steamApps, "common", installDir);
                if (IsGameRoot(root))
                {
                    return root;
                }
            }

            return common;
        }

        private static IEnumerable<string> GetSteamAppsFolders()
        {
            var roots = new List<string>();
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "InstallPath", null) as string);
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string);

            foreach (var root in roots.ToArray())
            {
                var steamApps = Path.Combine(root, "steamapps");
                if (Directory.Exists(steamApps))
                {
                    yield return steamApps;
                }

                var libraryFolders = Path.Combine(steamApps, "libraryfolders.vdf");
                if (!File.Exists(libraryFolders))
                {
                    continue;
                }

                foreach (var path in ReadSteamLibraryPaths(libraryFolders))
                {
                    var librarySteamApps = Path.Combine(path, "steamapps");
                    if (Directory.Exists(librarySteamApps))
                    {
                        yield return librarySteamApps;
                    }
                }
            }
        }

        private static void AddSteamRoot(List<string> roots, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var root = value.Replace('/', '\\');
            if (!roots.Contains(root) && Directory.Exists(root))
            {
                roots.Add(root);
            }
        }

        private static IEnumerable<string> ReadSteamLibraryPaths(string libraryFolders)
        {
            foreach (var line in File.ReadAllLines(libraryFolders))
            {
                var match = Regex.Match(line, "\"path\"\\s+\"(?<path>.+?)\"");
                if (!match.Success)
                {
                    continue;
                }

                yield return match.Groups["path"].Value.Replace(@"\\", @"\");
            }
        }

        private static string ReadAcfValue(string path, string key)
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var match = Regex.Match(line, "\"" + Regex.Escape(key) + "\"\\s+\"(?<value>.*?)\"", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups["value"].Value;
                }
            }

            return string.Empty;
        }

        private static string PluginPath(string gameRoot)
        {
            return Path.Combine(gameRoot, "BepInEx", "plugins", PluginFileName);
        }

        private static string PluginDisabledPath(string gameRoot)
        {
            return Path.Combine(gameRoot, "BepInEx", "plugins", PluginDisabledFileName);
        }

        private static string ConfigPath(string gameRoot)
        {
            return Path.Combine(gameRoot, "BepInEx", "config", PluginGuid + ".cfg");
        }

        private static string PayloadDir()
        {
            if (!string.IsNullOrEmpty(payloadDir))
            {
                return payloadDir;
            }

            var externalPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload");
            if (Directory.Exists(externalPayload) && File.Exists(Path.Combine(externalPayload, PluginFileName)))
            {
                payloadDir = externalPayload;
                return payloadDir;
            }

            var embeddedPayload = ExtractEmbeddedPayload();
            payloadDir = string.IsNullOrEmpty(embeddedPayload) ? externalPayload : embeddedPayload;
            return payloadDir;
        }

        private static string ExtractEmbeddedPayload()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(EmbeddedPayloadResourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                byte[] payloadBytes;
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    payloadBytes = memory.ToArray();
                }

                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AfterTheFallVRModKit",
                    "payload");
                var marker = Path.Combine(root, ".payload-version");
                var stamp = EmbeddedPayloadResourceName + ":" + ComputeSha256(payloadBytes);

                if (Directory.Exists(root) && File.Exists(marker) && string.Equals(File.ReadAllText(marker), stamp, StringComparison.Ordinal))
                {
                    return root;
                }

                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }

                Directory.CreateDirectory(root);
                using (var payloadStream = new MemoryStream(payloadBytes))
                {
                    ExtractZipStream(payloadStream, root);
                }

                File.WriteAllText(marker, stamp);
                return root;
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                return Convert.ToBase64String(sha256.ComputeHash(bytes));
            }
        }

        private static void ExtractZipStream(Stream stream, string destinationRoot)
        {
            var destinationFullPath = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                    if (!targetPath.StartsWith(destinationFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Embedded payload contains an invalid path: " + entry.FullName);
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    var targetDirectory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    using (var input = entry.Open())
                    using (var output = File.Create(targetPath))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        private static string StateFromFiles(string active, string disabled)
        {
            if (File.Exists(active))
            {
                return "enabled";
            }

            if (File.Exists(disabled))
            {
                return "disabled";
            }

            return "not installed";
        }

        private static void MoveFile(string source, string destination)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = directory.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destDir, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(destDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private void Log(string message)
        {
            logText.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }

        private sealed class FeatureConfig
        {
            public bool DisableInGameVoip;
            public bool SuppressClientBloodAndGore;
            public bool CleanupRetainedServerGame;

            public static FeatureConfig Defaults()
            {
                return new FeatureConfig
                {
                    DisableInGameVoip = true,
                    SuppressClientBloodAndGore = true,
                    CleanupRetainedServerGame = true
                };
            }
        }
    }
}
