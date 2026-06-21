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
using System.Threading.Tasks;
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
        private const string LegacyPluginFileName = "ATFNoVoip.dll";
        private const string LegacyPluginDisabledFileName = "ATFNoVoip.dll.disabled";
        private const string EmbeddedPayloadResourceName = "AfterTheFallVRModKit.Payload.zip";
        private const string DisconnectTimeoutLaunchOption = "-disconnectTimeout";
        private const int DefaultDisconnectTimeoutSeconds = 30;
        private const int MinDisconnectTimeoutSeconds = 5;
        private const int MaxDisconnectTimeoutSeconds = 120;
        private const string QuestPackageName = "com.vertigogames.atf";
        private const string VerifiedQuestVersionCode = "38148";
        private const string VerifiedQuestVersionName = "1.38147.41947";
        private const string QuestLibIl2CppEntryName = "lib/arm64-v8a/libil2cpp.so";
        private const string QuestUnityEntryName = "lib/arm64-v8a/libunity.so";
        private const string QuestMetadataEntryName = "assets/bin/Data/Managed/Metadata/global-metadata.dat";
        private const string QuestObbDirectory = "/sdcard/Android/obb/com.vertigogames.atf";
        private const string QuestObbParentDirectory = "/sdcard/Android/obb";
        private const string QuestObbBackupRoot = "/sdcard/Download/AfterTheFallVRModKit";
        private const string QuestObbName = "main.38148.com.vertigogames.atf.obb";
        private const string QuestBloodlessObbName = "main.38148.com.vertigogames.atf.bloodless.obb";
        private const string QuestObbRemotePath = QuestObbDirectory + "/" + QuestObbName;

        private readonly TextBox gamePathText;
        private readonly Label statusLabel;
        private readonly Label adminLabel;
        private readonly CheckBox bepInExCheck;
        private readonly CheckBox pluginCheck;
        private readonly CheckBox voipCheck;
        private readonly CheckBox bloodCheck;
        private readonly CheckBox doorbellWaveSoundCheck;
        private readonly CheckBox comfortEnemyVisualsCheck;
        private readonly CheckBox serverCleanupCheck;
        private readonly CheckBox vrPerfKitCheck;
        private readonly CheckBox disconnectTimeoutCheck;
        private readonly NumericUpDown disconnectTimeoutSeconds;
        private readonly TextBox logText;
        private readonly Button applyButton;
        private readonly Button refreshButton;
        private readonly Button browseButton;
        private readonly Button openFolderButton;
        private readonly Button launchButton;
        private readonly Button adbStatusButton;
        private readonly Button patchQuestApkButton;
        private readonly Button installQuestApkButton;
        private readonly Button adminButton;
        private readonly Label operationStatusLabel;
        private readonly ProgressBar operationProgressBar;
        private bool operationRunning;
        private static string payloadDir;
        private static readonly Encoding BytePreservingEncoding = Encoding.GetEncoding(28591);
        private static readonly string[] QuestBloodSettingsObbEntries = new[]
        {
            "assets/bin/Data/541ba57ea63899e478c25da546f15ed9",
            "assets/bin/Data/eccc90d64e804de4ba7eb24708909a7b"
        };
        private static readonly string[] QuestZombieDeathSettingsObbEntries = new[]
        {
            "assets/bin/Data/34371bffbaebc5b43be10f0d2ca3d2f0",
            "assets/bin/Data/5f8e8990ebe7b194c9b3f71f884e565d"
        };
        private static readonly string[] QuestImpactSettingsObbEntries = new[]
        {
            "assets/bin/Data/09772e8d18fb0ba4c922b384b54826a7",
            "assets/bin/Data/2f43c97460f90344e890c1ab531d42eb",
            "assets/bin/Data/30f12ef5552b8d04f8d84053f3a2d6e8",
            "assets/bin/Data/4e360a2e0d6cc4343bfb0d8e576594cc",
            "assets/bin/Data/5c23bb0e163aae14da39a0111525dc74",
            "assets/bin/Data/809fa95a6f185484d9bca2ba75980bde",
            "assets/bin/Data/84c9c1bf03bf08a42bc2622234e637cc",
            "assets/bin/Data/ba2389a2b6d8c384fa8ac8843505ddd0",
            "assets/bin/Data/deaeba1cfa1a0274fb76a3e9ce834770",
            "assets/bin/Data/e5554a9465c02da498404024ec8fb02c",
            "assets/bin/Data/eb4d35904fc40c14095316abef7f47d9",
            "assets/bin/Data/eb60c4d26e4485b48b7e8c5a368ef995",
            "assets/bin/Data/20d7b4b282b28ea43aa03ab4dddd00f0",
            "assets/bin/Data/998e200d03c67f34b90e8ca5b36a7991"
        };
        private static readonly string[] QuestBloodTextureArrayFields = new[]
        {
            "bloodPoolTextures",
            "straightDecalTextures",
            "angledDecalTextures",
            "indirectSplatterDecalTextures",
            "zombieBloodDecalTextures",
            "gibFloorBloodTextures",
            "gibSplatterBloodTextures"
        };
        private static readonly byte[] Arm64ReturnInstruction = new byte[] { 0xC0, 0x03, 0x5F, 0xD6 };
        private static readonly QuestPatchTarget[] QuestPatchTargets = new QuestPatchTarget[]
        {
            new QuestPatchTarget("Blood", "BloodPoolPainterModule", "PaintBloodGroundBelowPosition", 0x41FF6C8L, new byte[] { 0xFF, 0xC3, 0x02, 0xD1 }),
            new QuestPatchTarget("Blood", "BloodPoolPainterModule", "PaintBloodPoolOnHips", 0x41FF420L, new byte[] { 0xFF, 0x83, 0x01, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintBloodDecal", 0x48D1F90L, new byte[] { 0xFF, 0x03, 0x05, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintBloodDecal", 0x48D2FFCL, new byte[] { 0xFF, 0x03, 0x06, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintBloodDecalNow", 0x48D1568L, new byte[] { 0xFC, 0x6B, 0xBB, 0xA9 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintBloodPool", 0x48D2C90L, new byte[] { 0xFF, 0xC3, 0x05, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintBulletBlood", 0x48D19E4L, new byte[] { 0xFF, 0x83, 0x02, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintGibFloorBlood", 0x48D263CL, new byte[] { 0xFF, 0xC3, 0x01, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintGibSplatterBlood", 0x48D27A8L, new byte[] { 0xFF, 0xC3, 0x01, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintImpactBlood", 0x48D2928L, new byte[] { 0xFF, 0xC3, 0x02, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintIndirectSplatterBlood", 0x48D2104L, new byte[] { 0xFF, 0xC3, 0x01, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintQueuedBloodDecal", 0x48D17DCL, new byte[] { 0xFF, 0x03, 0x02, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.BloodPainter", "PaintSplatterBlood", 0x48D2264L, new byte[] { 0xFF, 0xC3, 0x02, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.ClientEnemyNetworking", "HandleEnemyGibNetworkMessage", 0x421F828L, new byte[] { 0xFF, 0x83, 0x02, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.PaintBloodOnCollisionBehaviour", "TryPaintBlood", 0x42135F8L, new byte[] { 0xFF, 0x03, 0x03, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.Zombies.ZombieBloodMaskPainter", "PaintBlood", 0x42FEAFCL, new byte[] { 0xEE, 0x0F, 0x17, 0xFC }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.Zombies.ZombieMutilationView", "PaintMutilationBlood", 0x42DE26CL, new byte[] { 0xFF, 0x43, 0x04, 0xD1 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule", "ApplyMutilationEffect", 0x42F2570L, new byte[] { 0xEF, 0x3B, 0xB6, 0x6D }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule", "HandleZombieHitEvent", 0x42F0618L, new byte[] { 0xFC, 0x5B, 0xBD, 0xA9 }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule", "OnHitImpact", 0x42F1B04L, new byte[] { 0xEF, 0x3B, 0xB6, 0x6D }),
            new QuestPatchTarget("Blood", "Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule", "OnImpact", 0x42F06C0L, new byte[] { 0xFC, 0x0F, 0x1D, 0xF8 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.Fmod.FmodVoipRecorder", "Update", 0x5C109A4L, new byte[] { 0xFF, 0x83, 0x02, 0xD1 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.Fmod.FmodVoipRecorder", "UpdateRecordDriver", 0x5C100D4L, new byte[] { 0xF5, 0x53, 0xBE, 0xA9 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.Fmod.FmodVoipRecorder", "UpdateRecordDriver", 0x5C10138L, new byte[] { 0xFF, 0x43, 0x01, 0xD1 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.Fmod.FmodVoipRecorder", "VoipThread", 0x5C10840L, new byte[] { 0xF5, 0x53, 0xBE, 0xA9 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.VoipClient", "HandleVoipInitPacket", 0x5C09724L, new byte[] { 0xFF, 0x03, 0x03, 0xD1 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.VoipClient", "HandleVoipInitResponsePacket", 0x5C0A6D4L, new byte[] { 0xFF, 0x83, 0x02, 0xD1 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.VoipClient", "JoinChannel", 0x5C0757CL, new byte[] { 0xF9, 0x63, 0xBC, 0xA9 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.VoipClient", "SetVolumeOther", 0x5C07110L, new byte[] { 0xFF, 0x03, 0x01, 0xD1 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.VoipRemotePeer", "HandleJoinedChannelPacket", 0x5C0C46CL, new byte[] { 0xFB, 0x6B, 0xBB, 0xA9 }),
            new QuestPatchTarget("VOIP", "Vertigo.Voip.VoipRemotePeer", "SetVolume", 0x5C06F7CL, new byte[] { 0xFF, 0x43, 0x01, 0xD1 })
        };

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
            root.RowCount = 8;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
            toggles.Dock = DockStyle.Top;
            toggles.Height = 390;
            toggles.MinimumSize = new Size(0, 360);
            toggles.Padding = new Padding(12);

            var toggleScrollPanel = new Panel();
            toggleScrollPanel.Dock = DockStyle.Fill;
            toggleScrollPanel.AutoScroll = true;
            toggles.Controls.Add(toggleScrollPanel);

            var toggleLayout = new TableLayoutPanel();
            toggleLayout.AutoSize = true;
            toggleLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toggleLayout.Dock = DockStyle.Top;
            toggleLayout.ColumnCount = 1;
            toggleLayout.RowCount = 9;
            for (var i = 0; i < 9; i++)
            {
                toggleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            toggleScrollPanel.Controls.Add(toggleLayout);

            bepInExCheck = MakeCheckBox("BepInEx mod loader", "Installs or enables the IL2CPP mod loader used by the patch.");
            pluginCheck = MakeCheckBox("After The Fall VR Mod Kit", "Installs or enables the bundled VR mod kit plugin.");
            voipCheck = MakeCheckBox("Disable in-game VOIP", "Skips the game's built-in VOIP handlers. Use external voice chat instead.");
            bloodCheck = MakeCheckBox("Suppress client blood/gore visuals", "Skips client-side blood, decal, gib, mutilation, and known blood particle effects.");
            doorbellWaveSoundCheck = MakeCheckBox("Doorbell wave sound", "Replaces the harsh horde wave-start screech with a short ding dong doorbell tone.");
            comfortEnemyVisualsCheck = MakeCheckBox("Nephew Mode", "PC-only cosmetic mode that makes enemies look less scary with a bland plastic skin while keeping game hitboxes and behavior intact.");
            serverCleanupCheck = MakeCheckBox("Clean retained ServerGame on hub return", "Experimental cleanup for the ServerGame memory leak seen after horde.");
            vrPerfKitCheck = MakeCheckBox("vrperfkit injection", "Enables dxgi.dll based vrperfkit injection when the package or an existing install is present.");

            var timeoutPanel = new FlowLayoutPanel();
            timeoutPanel.AutoSize = true;
            timeoutPanel.Dock = DockStyle.Top;
            timeoutPanel.FlowDirection = FlowDirection.LeftToRight;
            timeoutPanel.WrapContents = false;
            timeoutPanel.Margin = new Padding(0, 0, 0, 0);

            var timeoutTooltip = "Adds " + DisconnectTimeoutLaunchOption + " to Steam launch options so the PC host waits longer before dropping stalled connections.";
            disconnectTimeoutCheck = MakeCheckBox("Set network disconnect timeout", timeoutTooltip);
            disconnectTimeoutCheck.Margin = new Padding(0, 6, 12, 6);
            timeoutPanel.Controls.Add(disconnectTimeoutCheck);

            disconnectTimeoutSeconds = new NumericUpDown();
            disconnectTimeoutSeconds.Minimum = MinDisconnectTimeoutSeconds;
            disconnectTimeoutSeconds.Maximum = MaxDisconnectTimeoutSeconds;
            disconnectTimeoutSeconds.Increment = 5;
            disconnectTimeoutSeconds.Value = DefaultDisconnectTimeoutSeconds;
            disconnectTimeoutSeconds.Width = 70;
            disconnectTimeoutSeconds.Enabled = false;
            disconnectTimeoutSeconds.Margin = new Padding(0, 4, 6, 4);
            new ToolTip().SetToolTip(disconnectTimeoutSeconds, timeoutTooltip);
            timeoutPanel.Controls.Add(disconnectTimeoutSeconds);

            var secondsLabel = new Label();
            secondsLabel.Text = "seconds";
            secondsLabel.AutoSize = true;
            secondsLabel.Margin = new Padding(0, 8, 0, 4);
            timeoutPanel.Controls.Add(secondsLabel);

            disconnectTimeoutCheck.CheckedChanged += delegate
            {
                disconnectTimeoutSeconds.Enabled = disconnectTimeoutCheck.Checked;
            };

            toggleLayout.Controls.Add(MakeToggleRow(bepInExCheck));
            toggleLayout.Controls.Add(MakeToggleRow(pluginCheck));
            toggleLayout.Controls.Add(MakeToggleRow(voipCheck));
            toggleLayout.Controls.Add(MakeToggleRow(bloodCheck));
            toggleLayout.Controls.Add(MakeToggleRow(doorbellWaveSoundCheck));
            toggleLayout.Controls.Add(MakeToggleRow(comfortEnemyVisualsCheck));
            toggleLayout.Controls.Add(MakeToggleRow(serverCleanupCheck));
            toggleLayout.Controls.Add(MakeToggleRow(vrPerfKitCheck));
            toggleLayout.Controls.Add(MakeToggleRow(timeoutPanel, timeoutTooltip));
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

            adbStatusButton = new Button();
            adbStatusButton.Text = "ADB Status";
            adbStatusButton.AutoSize = true;
            adbStatusButton.Height = 32;
            adbStatusButton.Click += delegate { ShowAdbStatus(); };
            actionPanel.Controls.Add(adbStatusButton);

            patchQuestApkButton = new Button();
            patchQuestApkButton.Text = "Create Quest OBB";
            patchQuestApkButton.AutoSize = true;
            patchQuestApkButton.Height = 32;
            patchQuestApkButton.Click += delegate { PatchQuestObb(); };
            actionPanel.Controls.Add(patchQuestApkButton);

            installQuestApkButton = new Button();
            installQuestApkButton.Text = "Install Quest OBB";
            installQuestApkButton.AutoSize = true;
            installQuestApkButton.Height = 32;
            installQuestApkButton.Click += delegate { InstallQuestObbPatch(); };
            actionPanel.Controls.Add(installQuestApkButton);

            adminButton = new Button();
            adminButton.Text = "Restart as Admin";
            adminButton.AutoSize = true;
            adminButton.Height = 32;
            adminButton.Click += delegate { RestartAsAdmin(); };
            actionPanel.Controls.Add(adminButton);
            root.Controls.Add(actionPanel);

            var progressPanel = new TableLayoutPanel();
            progressPanel.AutoSize = true;
            progressPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            progressPanel.Dock = DockStyle.Top;
            progressPanel.ColumnCount = 2;
            progressPanel.RowCount = 1;
            progressPanel.Margin = new Padding(0, 0, 0, 8);
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            operationStatusLabel = new Label();
            operationStatusLabel.Text = "Ready.";
            operationStatusLabel.AutoSize = true;
            operationStatusLabel.ForeColor = Color.DimGray;
            operationStatusLabel.Margin = new Padding(0, 4, 8, 0);
            progressPanel.Controls.Add(operationStatusLabel, 0, 0);

            operationProgressBar = new ProgressBar();
            operationProgressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            operationProgressBar.Dock = DockStyle.None;
            operationProgressBar.Width = 180;
            operationProgressBar.Height = 18;
            operationProgressBar.Style = ProgressBarStyle.Blocks;
            operationProgressBar.MarqueeAnimationSpeed = 0;
            operationProgressBar.Margin = new Padding(0, 3, 0, 0);
            progressPanel.Controls.Add(operationProgressBar, 1, 0);
            root.Controls.Add(progressPanel);

            logText = new TextBox();
            logText.Dock = DockStyle.Fill;
            logText.Multiline = true;
            logText.ReadOnly = true;
            logText.ScrollBars = ScrollBars.Vertical;
            logText.Font = new Font("Consolas", 9);
            logText.MinimumSize = new Size(0, 120);
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
            checkBox.Margin = new Padding(0, 0, 0, 2);
            checkBox.Tag = tooltip;
            new ToolTip().SetToolTip(checkBox, tooltip);
            return checkBox;
        }

        private static Control MakeToggleRow(CheckBox checkBox)
        {
            return MakeToggleRow(checkBox, checkBox.Tag as string);
        }

        private static Control MakeToggleRow(Control control, string description)
        {
            var panel = new TableLayoutPanel();
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Dock = DockStyle.Top;
            panel.ColumnCount = 1;
            panel.RowCount = 2;
            panel.Margin = new Padding(0, 2, 0, 8);
            panel.Padding = new Padding(0);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            control.Margin = new Padding(0, 0, 0, 2);
            panel.Controls.Add(control, 0, 0);

            var descriptionLabel = new Label();
            descriptionLabel.Text = description ?? string.Empty;
            descriptionLabel.AutoSize = true;
            descriptionLabel.ForeColor = Color.DimGray;
            descriptionLabel.Margin = new Padding(22, 0, 0, 0);
            descriptionLabel.MaximumSize = new Size(620, 0);
            new ToolTip().SetToolTip(descriptionLabel, descriptionLabel.Text);
            panel.Controls.Add(descriptionLabel, 0, 1);

            return panel;
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
                doorbellWaveSoundCheck.Checked = config.DoorbellWaveSound;
                comfortEnemyVisualsCheck.Checked = config.ComfortEnemyVisuals;
                serverCleanupCheck.Checked = config.CleanupRetainedServerGame;

                var launchOptions = ReadSteamLaunchOptions();
                var timeoutMilliseconds = ParseDisconnectTimeoutMilliseconds(launchOptions.Options);
                if (timeoutMilliseconds > 0)
                {
                    disconnectTimeoutCheck.Checked = true;
                    disconnectTimeoutSeconds.Value = ClampTimeoutSeconds(timeoutMilliseconds / 1000);
                }
                else
                {
                    disconnectTimeoutCheck.Checked = false;
                    disconnectTimeoutSeconds.Value = DefaultDisconnectTimeoutSeconds;
                }

                disconnectTimeoutSeconds.Enabled = disconnectTimeoutCheck.Checked;

                Log("Status refreshed for " + gameRoot);
                Log("BepInEx: " + StateFromFiles(Path.Combine(gameRoot, "winhttp.dll"), Path.Combine(gameRoot, "winhttp.dll.disabled")));
                Log("VR mod kit: " + StateFromFiles(PluginPath(gameRoot), PluginDisabledPath(gameRoot)));
                Log("vrperfkit: " + StateFromFiles(Path.Combine(gameRoot, "dxgi.dll"), Path.Combine(gameRoot, "dxgi.dll.disabled")));
                Log(launchOptions.Found
                    ? "Steam launch options: " + (string.IsNullOrEmpty(launchOptions.Options) ? "<empty>" : launchOptions.Options)
                    : "Steam launch options: not found for app " + AppId);
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
                    DoorbellWaveSound = doorbellWaveSoundCheck.Checked,
                    ComfortEnemyVisuals = comfortEnemyVisualsCheck.Checked,
                    CleanupRetainedServerGame = serverCleanupCheck.Checked
                });
                ApplyVrPerfKit(gameRoot, vrPerfKitCheck.Checked);
                ApplyDisconnectTimeoutLaunchOption(disconnectTimeoutCheck.Checked, (int)disconnectTimeoutSeconds.Value);

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
                DisableLegacyPlugin(gameRoot);
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

        private void DisableLegacyPlugin(string gameRoot)
        {
            var active = Path.Combine(gameRoot, "BepInEx", "plugins", LegacyPluginFileName);
            var disabled = Path.Combine(gameRoot, "BepInEx", "plugins", LegacyPluginDisabledFileName);
            if (!File.Exists(active))
            {
                return;
            }

            MoveFile(active, disabled);
            Log("Disabled legacy ATFNoVoip.dll because the VR Mod Kit replaces it.");
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
                else if (string.Equals(key, "DoorbellWaveSound", StringComparison.OrdinalIgnoreCase))
                {
                    result.DoorbellWaveSound = parsed;
                }
                else if (string.Equals(key, "ComfortEnemyVisuals", StringComparison.OrdinalIgnoreCase))
                {
                    result.ComfortEnemyVisuals = parsed;
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
            builder.AppendLine("## Skip client-side blood, decal, gib, mutilation, and known blood particle effects.");
            builder.AppendLine("SuppressClientBloodAndGore = " + config.SuppressClientBloodAndGore.ToString().ToLowerInvariant());
            builder.AppendLine();
            builder.AppendLine("## Replace the harsh horde wave-start stinger with a short doorbell-like ding dong tone.");
            builder.AppendLine("DoorbellWaveSound = " + config.DoorbellWaveSound.ToString().ToLowerInvariant());
            builder.AppendLine();
            builder.AppendLine("## Nephew Mode: make enemies look less scary with a bland plastic skin. Cosmetic only; gameplay, hitboxes, AI, damage, and networking are untouched.");
            builder.AppendLine("ComfortEnemyVisuals = " + config.ComfortEnemyVisuals.ToString().ToLowerInvariant());
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

        private void ApplyDisconnectTimeoutLaunchOption(bool enabled, int timeoutSeconds)
        {
            var current = ReadSteamLaunchOptions();
            if (!current.Found)
            {
                if (!enabled)
                {
                    Log("Steam launch options not found; disconnect timeout update skipped.");
                    return;
                }

                throw new FileNotFoundException("Could not find Steam launch options for After The Fall. Open the game once from Steam, then try Apply again.");
            }

            var nextOptions = SetDisconnectTimeoutLaunchOption(current.Options, enabled ? timeoutSeconds * 1000 : 0);
            if (string.Equals(current.Options ?? string.Empty, nextOptions, StringComparison.Ordinal))
            {
                Log("Steam launch options already match the selected disconnect timeout setting.");
                return;
            }

            WriteSteamLaunchOptions(current.LocalConfigPath, nextOptions);
            Log("Updated Steam launch options: " + (string.IsNullOrEmpty(nextOptions) ? "<empty>" : nextOptions));
        }

        private static int ClampTimeoutSeconds(int value)
        {
            if (value < MinDisconnectTimeoutSeconds)
            {
                return MinDisconnectTimeoutSeconds;
            }

            if (value > MaxDisconnectTimeoutSeconds)
            {
                return MaxDisconnectTimeoutSeconds;
            }

            return value;
        }

        private static int ParseDisconnectTimeoutMilliseconds(string launchOptions)
        {
            var match = Regex.Match(launchOptions ?? string.Empty, @"(?i)(^|\s)" + Regex.Escape(DisconnectTimeoutLaunchOption) + @"(?:\s+(?<value>\d+))?");
            if (!match.Success)
            {
                return 0;
            }

            int value;
            if (!int.TryParse(match.Groups["value"].Value, out value))
            {
                return DefaultDisconnectTimeoutSeconds * 1000;
            }

            return value;
        }

        private static string SetDisconnectTimeoutLaunchOption(string launchOptions, int timeoutMilliseconds)
        {
            var withoutTimeout = RemoveDisconnectTimeoutLaunchOption(launchOptions);
            if (timeoutMilliseconds <= 0)
            {
                return withoutTimeout;
            }

            var option = DisconnectTimeoutLaunchOption + " " + timeoutMilliseconds;
            return string.IsNullOrEmpty(withoutTimeout) ? option : withoutTimeout + " " + option;
        }

        private static string RemoveDisconnectTimeoutLaunchOption(string launchOptions)
        {
            var result = Regex.Replace(
                launchOptions ?? string.Empty,
                @"(?i)(^|\s)" + Regex.Escape(DisconnectTimeoutLaunchOption) + @"(?:\s+(?!-)\S+)?",
                delegate(Match match)
                {
                    return match.Groups[1].Value.Length == 0 ? string.Empty : " ";
                });

            return Regex.Replace(result, "\\s{2,}", " ").Trim();
        }

        private static SteamLaunchOptions ReadSteamLaunchOptions()
        {
            foreach (var path in FindSteamLocalConfigPaths())
            {
                string options;
                if (TryReadSteamLaunchOptions(path, out options))
                {
                    return new SteamLaunchOptions
                    {
                        Found = true,
                        LocalConfigPath = path,
                        Options = options
                    };
                }
            }

            return new SteamLaunchOptions
            {
                Found = false,
                LocalConfigPath = string.Empty,
                Options = string.Empty
            };
        }

        private static bool TryReadSteamLaunchOptions(string localConfigPath, out string options)
        {
            options = string.Empty;
            var lines = File.ReadAllLines(localConfigPath);
            int appLine;
            int openBraceLine;
            int closeBraceLine;
            if (!TryFindSteamAppBlock(lines, out appLine, out openBraceLine, out closeBraceLine))
            {
                return false;
            }

            for (var i = openBraceLine + 1; i < closeBraceLine; i++)
            {
                var match = Regex.Match(lines[i], "\"LaunchOptions\"\\s+\"(?<value>(?:\\\\.|[^\"])*)\"");
                if (match.Success)
                {
                    options = UnescapeVdfString(match.Groups["value"].Value);
                    return true;
                }
            }

            options = string.Empty;
            return true;
        }

        private static void WriteSteamLaunchOptions(string localConfigPath, string options)
        {
            var lines = new List<string>(File.ReadAllLines(localConfigPath));
            int appLine;
            int openBraceLine;
            int closeBraceLine;
            if (!TryFindSteamAppBlock(lines.ToArray(), out appLine, out openBraceLine, out closeBraceLine))
            {
                throw new InvalidOperationException("Could not find After The Fall app " + AppId + " in " + localConfigPath + ".");
            }

            var launchOptionsLine = -1;
            for (var i = openBraceLine + 1; i < closeBraceLine; i++)
            {
                if (Regex.IsMatch(lines[i], "\"LaunchOptions\"\\s+\""))
                {
                    launchOptionsLine = i;
                    break;
                }
            }

            var indent = launchOptionsLine >= 0
                ? LeadingWhitespace(lines[launchOptionsLine])
                : DetectAppValueIndent(lines, openBraceLine, closeBraceLine);
            var newLine = indent + "\"LaunchOptions\"\t\t\"" + EscapeVdfString(options ?? string.Empty) + "\"";

            File.Copy(localConfigPath, localConfigPath + ".vrmodkit.bak", true);
            if (launchOptionsLine >= 0)
            {
                lines[launchOptionsLine] = newLine;
            }
            else
            {
                lines.Insert(closeBraceLine, newLine);
            }

            File.WriteAllLines(localConfigPath, lines.ToArray(), Encoding.UTF8);
        }

        private static bool TryFindSteamAppBlock(string[] lines, out int appLine, out int openBraceLine, out int closeBraceLine)
        {
            appLine = -1;
            openBraceLine = -1;
            closeBraceLine = -1;

            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], "^\\s*\"" + Regex.Escape(AppId) + "\"\\s*$"))
                {
                    continue;
                }

                var open = NextNonBlankLine(lines, i + 1);
                if (open < 0 || !string.Equals(lines[open].Trim(), "{", StringComparison.Ordinal))
                {
                    continue;
                }

                var close = FindMatchingBrace(lines, open);
                if (close < 0)
                {
                    continue;
                }

                appLine = i;
                openBraceLine = open;
                closeBraceLine = close;
                return true;
            }

            return false;
        }

        private static int NextNonBlankLine(string[] lines, int start)
        {
            for (var i = start; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindMatchingBrace(string[] lines, int openBraceLine)
        {
            var depth = 0;
            for (var i = openBraceLine; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.Equals(trimmed, "{", StringComparison.Ordinal))
                {
                    depth++;
                }
                else if (string.Equals(trimmed, "}", StringComparison.Ordinal))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string DetectAppValueIndent(List<string> lines, int openBraceLine, int closeBraceLine)
        {
            for (var i = openBraceLine + 1; i < closeBraceLine; i++)
            {
                if (Regex.IsMatch(lines[i], "^\\s*\"[^\"]+\"\\s+\""))
                {
                    return LeadingWhitespace(lines[i]);
                }
            }

            return LeadingWhitespace(lines[openBraceLine]) + "\t";
        }

        private static string LeadingWhitespace(string value)
        {
            var count = 0;
            while (count < value.Length && char.IsWhiteSpace(value[count]))
            {
                count++;
            }

            return value.Substring(0, count);
        }

        private static string EscapeVdfString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string UnescapeVdfString(string value)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < (value ?? string.Empty).Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    i++;
                }

                builder.Append(value[i]);
            }

            return builder.ToString();
        }

        private static IEnumerable<string> FindSteamLocalConfigPaths()
        {
            foreach (var root in GetSteamInstallRoots())
            {
                var userdata = Path.Combine(root, "userdata");
                if (!Directory.Exists(userdata))
                {
                    continue;
                }

                foreach (var userDir in Directory.GetDirectories(userdata))
                {
                    var localConfig = Path.Combine(userDir, "config", "localconfig.vdf");
                    if (File.Exists(localConfig))
                    {
                        yield return localConfig;
                    }
                }
            }
        }

        private void ShowAdbStatus()
        {
            try
            {
                var adb = FindAdb();
                var devices = RunProcess(adb, "devices -l", 15000);
                Log("ADB devices:");
                Log(devices.Trim());

                var packagePath = RunProcess(adb, "shell pm path " + QuestPackageName, 15000).Trim();
                if (packagePath.Length == 0)
                {
                    Log("Quest package " + QuestPackageName + " was not found.");
                    MessageBox.Show(this, "ADB is available, but " + QuestPackageName + " was not found on the connected device.", "ADB Status", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var details = RunProcess(adb, "shell dumpsys package " + QuestPackageName, 15000);
                Log("Quest package path: " + packagePath);
                foreach (var line in details.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.IndexOf("versionName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("versionCode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("primaryCpuAbi", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log(line.Trim());
                    }
                }

                MessageBox.Show(this, "ADB is connected and " + QuestPackageName + " was found.", "ADB Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("ADB status failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "ADB Status failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void PatchQuestObb()
        {
            if (operationRunning)
            {
                return;
            }

            var warning = "This will pull the installed Quest OBB, patch a local copy of the blood/decal tuning data, and leave the official APK untouched.\r\n\r\n" +
                "It will not install anything on the headset.\r\n\r\n" +
                "Continue and create a patched OBB file?";
            if (MessageBox.Show(this, warning, "Create Quest OBB", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                Log("Quest OBB patch cancelled.");
                return;
            }

            await RunQuestOperation(
                "Creating patched Quest OBB...",
                delegate (IProgress<string> progress)
                {
                    var result = CreatePatchedQuestObbFromQuest(progress);
                    progress.Report("Patched Quest OBB created.");
                    return
                        "Patched Quest OBB created:\r\n" + result.PatchedObb + "\r\n\r\n" +
                        "Patch report:\r\n" + result.ReportCsv + "\r\n\r\n" +
                        "Patched fields: " + result.PatchedFieldCount + "\r\n\r\n" +
                        "The official Quest APK was not modified.";
                },
                "Quest OBB Created",
                "Quest OBB patch failed");
        }

        private async void InstallQuestObbPatch()
        {
            if (operationRunning)
            {
                return;
            }

            var warning = "This will patch and install the Quest OBB only.\r\n\r\n" +
                "The manager will reuse the newest local patched OBB when possible. If no patched OBB exists, it will pull the current OBB, patch blood/decal tuning data, back up the current headset OBB to /sdcard/Download/AfterTheFallVRModKit/obb-backup, and push the patched OBB back.\r\n\r\n" +
                "It does not uninstall or re-sign the APK. Continue?";

            if (MessageBox.Show(this, warning, "Install Quest OBB", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                Log("Quest OBB install cancelled.");
                return;
            }

            await RunQuestOperation(
                "Preparing Quest OBB patch...",
                delegate (IProgress<string> progress)
                {
                    var result = EnsurePatchedQuestObbForInstall(progress);
                    progress.Report("Installing patched Quest OBB...");
                    return InstallPatchedQuestObb(result, progress);
                },
                "Quest OBB Install Complete",
                "Quest OBB install failed");
        }

        private QuestObbPatchResult CreatePatchedQuestObbFromQuest(IProgress<string> progress)
        {
            var adb = FindAdb();
            progress.Report("Checking ADB connection...");
            EnsureAdbDevice(adb);

            EnsureVerifiedQuestPackage(adb, progress);

            if (!RemoteFileExists(adb, QuestObbRemotePath))
            {
                throw new FileNotFoundException("Quest OBB was not found at " + QuestObbRemotePath + ".");
            }

            var remoteObbSize = GetRemoteFileSize(adb, QuestObbRemotePath);
            var outputRoot = GetQuestObbOutputDirectory();
            Directory.CreateDirectory(outputRoot);
            var localSourceObb = Path.Combine(outputRoot, QuestObbName);
            DeleteIfExists(localSourceObb);

            progress.Report("Pulling Quest OBB...");
            RunProcessWithLocalFileProgress(
                adb,
                "pull " + QuoteArgument(QuestObbRemotePath) + " " + QuoteArgument(localSourceObb),
                900000,
                localSourceObb,
                remoteObbSize,
                "Pulling Quest OBB",
                progress);

            progress.Report("Patching Quest OBB data...");
            return CreatePatchedQuestObb(localSourceObb);
        }

        private QuestObbPatchResult EnsurePatchedQuestObbForInstall(IProgress<string> progress)
        {
            progress.Report("Looking for cached patched Quest OBB...");
            var cachedPatchedObb = TryFindLatestPatchedQuestObb();
            if (!string.IsNullOrEmpty(cachedPatchedObb) && IsReadableQuestObb(cachedPatchedObb))
            {
                progress.Report("Using cached patched Quest OBB.");
                return new QuestObbPatchResult
                {
                    PatchedObb = cachedPatchedObb,
                    ReportCsv = TryFindLatestQuestObbPatchReport(),
                    PatchedFieldCount = 0,
                    UsedCachedPatchedObb = true
                };
            }

            progress.Report("No cached patched Quest OBB found; creating one.");
            return CreatePatchedQuestObbFromQuest(progress);
        }

        private QuestObbPatchResult CreatePatchedQuestObb(string sourceObb)
        {
            var outputDir = GetQuestObbPatchedOutputDirectory();
            Directory.CreateDirectory(outputDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var patchedObb = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(QuestBloodlessObbName) + "-" + stamp + ".obb");
            var reportCsv = Path.Combine(outputDir, "obb-patch-report-" + stamp + ".csv");
            var reportRows = new List<QuestObbPatchReportRow>();

            File.Copy(sourceObb, patchedObb, true);
            using (var archive = ZipFile.Open(patchedObb, ZipArchiveMode.Update))
            {
                foreach (var entryName in QuestBloodSettingsObbEntries)
                {
                    UpdateQuestObbEntry(archive, entryName, delegate (string text)
                    {
                        return PatchQuestBloodSettingsText(text, entryName, reportRows);
                    });
                }

                foreach (var entryName in QuestZombieDeathSettingsObbEntries)
                {
                    UpdateQuestObbEntry(archive, entryName, delegate (string text)
                    {
                        return PatchQuestZombieDeathSettingsText(text, entryName, reportRows);
                    });
                }

                foreach (var entryName in QuestImpactSettingsObbEntries)
                {
                    UpdateQuestObbEntry(archive, entryName, delegate (string text)
                    {
                        return PatchQuestImpactSettingsText(text, entryName, reportRows);
                    });
                }
            }

            WriteQuestObbPatchReport(reportCsv, reportRows);

            var patchedFieldCount = 0;
            foreach (var row in reportRows)
            {
                patchedFieldCount += row.Count;
            }

            return new QuestObbPatchResult
            {
                PatchedObb = patchedObb,
                ReportCsv = reportCsv,
                PatchedFieldCount = patchedFieldCount
            };
        }

        private string InstallPatchedQuestObb(QuestObbPatchResult result, IProgress<string> progress)
        {
            var adb = FindAdb();
            progress.Report("Checking ADB connection...");
            EnsureAdbDevice(adb);
            EnsureVerifiedQuestPackage(adb, progress);
            if (!RemoteFileExists(adb, QuestObbRemotePath))
            {
                throw new FileNotFoundException("Quest OBB was not found at " + QuestObbRemotePath + ".");
            }

            var builder = new StringBuilder();
            builder.AppendLine("Patched OBB:");
            builder.AppendLine(result.PatchedObb);
            builder.AppendLine();
            if (!string.IsNullOrEmpty(result.ReportCsv))
            {
                builder.AppendLine("Patch report:");
                builder.AppendLine(result.ReportCsv);
                builder.AppendLine();
            }

            if (result.UsedCachedPatchedObb)
            {
                builder.AppendLine("Used cached patched OBB.");
                builder.AppendLine();
            }

            var patchedObbSize = new FileInfo(result.PatchedObb).Length;
            var remoteObbSize = GetRemoteFileSize(adb, QuestObbRemotePath);
            if (remoteObbSize == patchedObbSize)
            {
                progress.Report("Quest OBB already matches cached patched size; skipping push.");
                builder.AppendLine("Quest OBB already matches cached patched size.");
                builder.AppendLine("Skipped backup and push.");
                return builder.ToString();
            }

            progress.Report("Stopping Quest game package...");
            RunProcess(adb, "shell am force-stop " + QuestPackageName, 15000);

            progress.Report("Backing up current Quest OBB...");
            var backupDirectory = QuestObbBackupRoot + "/obb-backup";
            var backupPath = backupDirectory + "/" + QuestObbName + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            RunAdbShell(adb, "mkdir -p " + QuoteShellArgument(backupDirectory), 15000);
            RunAdbShell(adb, "cp " + QuoteShellArgument(QuestObbRemotePath) + " " + QuoteShellArgument(backupPath), 900000);
            builder.AppendLine("Remote backup:");
            builder.AppendLine(backupPath);
            builder.AppendLine();

            progress.Report("Pushing patched Quest OBB...");
            var remoteTempPath = QuestObbRemotePath + ".tmp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            RunAdbShell(adb, "rm -f " + QuoteShellArgument(remoteTempPath), 15000);
            RunProcessWithRemoteFileProgress(
                adb,
                "push " + QuoteArgument(result.PatchedObb) + " " + QuoteArgument(remoteTempPath),
                900000,
                remoteTempPath,
                patchedObbSize,
                "Pushing patched Quest OBB",
                progress);
            RunAdbShell(adb, "mv " + QuoteShellArgument(remoteTempPath) + " " + QuoteShellArgument(QuestObbRemotePath), 120000);
            builder.AppendLine("Installed patched OBB to:");
            builder.AppendLine(QuestObbRemotePath);
            builder.AppendLine();
            builder.AppendLine("The official Quest APK was not modified.");

            progress.Report("Quest OBB install complete.");
            return builder.ToString();
        }

        private async void PatchQuestApk()
        {
            if (operationRunning)
            {
                return;
            }

            var warning = "This will pull the installed Quest APK, patch a copy of libil2cpp.so, rebuild the APK, and sign it with a local debug key.\r\n\r\n" +
                "It will not install or uninstall anything on the headset. Use this when you only want a patched APK file for debugging or sharing.\r\n\r\n" +
                "Continue and create a patched APK file?";
            if (MessageBox.Show(this, warning, "Create Quest APK Only", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                Log("Quest APK patch cancelled.");
                return;
            }

            await RunQuestOperation(
                "Creating patched Quest APK...",
                delegate (IProgress<string> progress)
                {
                    var result = CreatePatchedQuestApkFromQuest(progress);
                    if (!IsQuestApkInstallLayoutReady(result.SignedApk))
                    {
                        throw new InvalidOperationException("The regenerated APK does not meet Android install layout requirements.");
                    }

                    progress.Report("Patched APK created.");
                    return
                        "Patched Quest APK created:\r\n" + result.SignedApk + "\r\n\r\n" +
                        "Patch report:\r\n" + result.ReportCsv + "\r\n\r\n" +
                        "Patched targets: " + result.PatchedCount + "\r\n\r\n" +
                        "This APK has not been installed.";
                },
                "Quest APK Created",
                "Quest APK patch failed");
        }

        private async void InstallPatchedQuestApk()
        {
            if (operationRunning)
            {
                return;
            }

            var warning = "This will patch and install the Quest APK.\r\n\r\n" +
                "The manager will reuse the newest install-ready patched APK when possible. Otherwise it will pull the installed APK, patch it, rebuild it, sign it, and then install it.\r\n\r\n" +
                "If Android rejects a normal update because the official store app has a different signature, it will move the OBB content folder to a temporary backup, uninstall the official app, install the patched APK, then restore the OBB folder.\r\n\r\n" +
                "Uninstalling can remove local app data or saves. Continue?";

            if (MessageBox.Show(this, warning, "Patch + Install Quest", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                Log("Quest APK install cancelled.");
                return;
            }

            await RunQuestOperation(
                "Preparing Quest APK install...",
                delegate (IProgress<string> progress)
                {
                    var apkPath = EnsureInstallReadyQuestApk(progress);
                    progress.Report("Installing patched Quest APK...");
                    return InstallPatchedQuestApk(apkPath, progress);
                },
                "Quest APK Install Complete",
                "Quest APK install failed");
        }

        private async Task RunQuestOperation(string startingStatus, Func<IProgress<string>, string> operation, string successTitle, string failureTitle)
        {
            SetOperationRunning(true, startingStatus);
            var progress = new Progress<string>(delegate (string message)
            {
                operationStatusLabel.Text = message;
                UpdateOperationProgressBarFromMessage(message);
                Log(message);
            });

            try
            {
                var report = await Task.Run(delegate
                {
                    return operation(progress);
                });

                Log(report);
                MessageBox.Show(this, report, successTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log(failureTitle + ": " + ex.Message);
                MessageBox.Show(this, ex.Message, failureTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetOperationRunning(false, "Ready.");
            }
        }

        private void SetOperationRunning(bool running, string status)
        {
            operationRunning = running;
            UseWaitCursor = running;
            operationStatusLabel.Text = status;
            if (running)
            {
                SetIndeterminateOperationProgress();
            }
            else
            {
                operationProgressBar.Style = ProgressBarStyle.Blocks;
                operationProgressBar.MarqueeAnimationSpeed = 0;
                operationProgressBar.Value = 0;
            }

            applyButton.Enabled = !running;
            refreshButton.Enabled = !running;
            browseButton.Enabled = !running;
            openFolderButton.Enabled = !running;
            launchButton.Enabled = !running;
            adbStatusButton.Enabled = !running;
            patchQuestApkButton.Enabled = !running;
            installQuestApkButton.Enabled = !running;
            adminButton.Enabled = !running;
        }

        private void UpdateOperationProgressBarFromMessage(string message)
        {
            var match = Regex.Match(message ?? string.Empty, @"\b(?<percent>\d{1,3})%\b");
            int percent;
            if (match.Success && int.TryParse(match.Groups["percent"].Value, out percent))
            {
                operationProgressBar.Style = ProgressBarStyle.Blocks;
                operationProgressBar.MarqueeAnimationSpeed = 0;
                operationProgressBar.Value = Math.Max(operationProgressBar.Minimum, Math.Min(operationProgressBar.Maximum, percent));
                return;
            }

            if (operationRunning)
            {
                SetIndeterminateOperationProgress();
            }
        }

        private void SetIndeterminateOperationProgress()
        {
            operationProgressBar.Style = ProgressBarStyle.Marquee;
            operationProgressBar.MarqueeAnimationSpeed = 30;
        }

        private QuestApkPatchResult CreatePatchedQuestApkFromQuest(IProgress<string> progress)
        {
            progress.Report("Pulling installed Quest APK...");
            var preflight = PullQuestApkFromQuest();
            progress.Report("Inspecting Quest APK...");

            if (!string.Equals(preflight.VersionCode, VerifiedQuestVersionCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Quest APK patching is currently verified only for versionCode " + VerifiedQuestVersionCode +
                    " / versionName " + VerifiedQuestVersionName + ". Detected versionCode " +
                    EmptyIfNull(preflight.VersionCode, "unknown") + " / versionName " +
                    EmptyIfNull(preflight.VersionName, "unknown") + ".");
            }

            progress.Report("Patching, rebuilding, aligning, and signing APK...");
            return CreatePatchedQuestApk(preflight.LocalApk);
        }

        private string EnsureInstallReadyQuestApk(IProgress<string> progress)
        {
            var apkPath = TryFindLatestPatchedQuestApk();
            if (!string.IsNullOrEmpty(apkPath))
            {
                progress.Report("Checking newest patched APK layout...");
                if (IsQuestApkInstallLayoutReady(apkPath))
                {
                    progress.Report("Using newest install-ready patched APK.");
                    return apkPath;
                }

                progress.Report("Newest patched APK needs regeneration.");
            }
            else
            {
                progress.Report("No patched APK found; creating one now.");
            }

            var result = CreatePatchedQuestApkFromQuest(progress);
            if (!IsQuestApkInstallLayoutReady(result.SignedApk))
            {
                throw new InvalidOperationException("The regenerated APK does not meet Android install layout requirements.");
            }

            progress.Report("Regenerated install-ready patched APK.");
            return result.SignedApk;
        }

        private string RunQuestApkPreflight()
        {
            return PullQuestApkFromQuest().Report;
        }

        private QuestApkPullResult PullQuestApkFromQuest()
        {
            var adb = FindAdb();
            EnsureAdbDevice(adb);

            var pathOutput = RunProcess(adb, "shell pm path " + QuestPackageName, 15000).Trim();
            if (!pathOutput.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(QuestPackageName + " was not found on the connected Quest.");
            }

            var remoteApk = pathOutput.Substring("package:".Length).Trim();
            var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AfterTheFallVRModKit", "quest-apk");
            Directory.CreateDirectory(outputDir);
            var localApk = Path.Combine(outputDir, QuestPackageName + "-base.apk");
            var details = RunProcess(adb, "shell dumpsys package " + QuestPackageName, 15000);
            RunProcess(adb, "pull \"" + remoteApk + "\" \"" + localApk + "\"", 120000);

            var versionCode = MatchValue(details, @"versionCode=(?<value>\d+)");
            var versionName = MatchValue(details, @"versionName=(?<value>[^\r\n]+)");
            var report = "Pulled Quest APK to:\r\n" + localApk + "\r\n\r\n" +
                "Package version:\r\n" +
                "- versionCode: " + EmptyIfNull(versionCode, "unknown") + "\r\n" +
                "- versionName: " + EmptyIfNull(versionName, "unknown") + "\r\n\r\n" +
                InspectQuestApk(localApk);

            return new QuestApkPullResult
            {
                LocalApk = localApk,
                VersionCode = versionCode,
                VersionName = versionName,
                Report = report
            };
        }

        private string InstallPatchedQuestApk(string apkPath, IProgress<string> progress)
        {
            var adb = FindAdb();
            progress.Report("Checking ADB connection...");
            EnsureAdbDevice(adb);

            var builder = new StringBuilder();
            builder.AppendLine("Patched APK:");
            builder.AppendLine(apkPath);
            builder.AppendLine();

            progress.Report("Trying normal APK update...");
            var firstInstall = RunProcessWithResult(adb, "install -r " + QuoteArgument(apkPath), 300000);
            if (firstInstall.ExitCode == 0)
            {
                progress.Report("Installed patched APK with normal update.");
                builder.AppendLine("Installed patched APK with a normal update.");
                return builder.ToString();
            }

            builder.AppendLine("Normal update was rejected:");
            builder.AppendLine(TrimProcessOutput(firstInstall));
            builder.AppendLine();

            if (!IsSignatureMismatchInstallFailure(firstInstall.CombinedOutput))
            {
                throw new InvalidOperationException("ADB install failed before uninstall, and it did not look like the expected signature mismatch:\r\n" + TrimProcessOutput(firstInstall));
            }

            progress.Report("Signature mismatch detected; replacing installed package.");
            builder.AppendLine("Signature mismatch detected. Replacing the installed package.");

            string backupDirectory = null;
            Exception restoreError = null;
            try
            {
                progress.Report("Checking Quest OBB folder...");
                if (RemoteDirectoryExists(adb, QuestObbDirectory))
                {
                    progress.Report("Moving OBB folder to temporary backup...");
                    backupDirectory = QuestObbBackupRoot + "/obb-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    RunAdbShell(adb, "mkdir -p " + QuoteShellArgument(QuestObbBackupRoot), 15000);
                    RunAdbShell(adb, "mv " + QuoteShellArgument(QuestObbDirectory) + " " + QuoteShellArgument(backupDirectory), 120000);
                    builder.AppendLine("Moved OBB content to temporary backup:");
                    builder.AppendLine(backupDirectory);
                }
                else
                {
                    builder.AppendLine("No Quest OBB folder was found at " + QuestObbDirectory + ".");
                }

                progress.Report("Uninstalling existing Quest package...");
                var uninstall = RunProcessWithResult(adb, "uninstall " + QuestPackageName, 120000);
                if (uninstall.ExitCode != 0 && TrimProcessOutput(uninstall).IndexOf("Unknown package", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException("ADB uninstall failed:\r\n" + TrimProcessOutput(uninstall));
                }

                builder.AppendLine("Uninstalled existing Quest package.");
                progress.Report("Installing patched Quest APK...");
                RunProcess(adb, "install " + QuoteArgument(apkPath), 300000);
                builder.AppendLine("Installed patched Quest APK.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(backupDirectory))
                {
                    try
                    {
                        progress.Report("Restoring Quest OBB folder...");
                        RestoreQuestObb(adb, backupDirectory);
                        builder.AppendLine("Restored OBB content to:");
                        builder.AppendLine(QuestObbDirectory);
                    }
                    catch (Exception ex)
                    {
                        restoreError = ex;
                    }
                }
            }

            if (restoreError != null)
            {
                throw new InvalidOperationException("The patched APK install ran, but OBB restore failed. Backup remains at " + backupDirectory + ". Restore error: " + restoreError.Message);
            }

            progress.Report("Quest APK install complete.");
            return builder.ToString();
        }

        private static string TryFindLatestPatchedQuestApk()
        {
            try
            {
                return FindLatestPatchedQuestApk();
            }
            catch
            {
                return null;
            }
        }

        private static string FindLatestPatchedQuestApk()
        {
            var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AfterTheFallVRModKit", "quest-apk", "patched");
            if (!Directory.Exists(outputDir))
            {
                throw new DirectoryNotFoundException("No patched APK folder was found. Click Patch + Install Quest or Create APK Only first.");
            }

            var files = Directory.GetFiles(outputDir, QuestPackageName + "-modded-*-signed.apk");
            if (files.Length == 0)
            {
                throw new FileNotFoundException("No signed patched APK was found. Click Patch + Install Quest or Create APK Only first.");
            }

            Array.Sort(files, delegate (string left, string right)
            {
                return File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left));
            });

            return files[0];
        }

        private static string TryFindLatestPatchedQuestObb()
        {
            var outputDir = GetQuestObbPatchedOutputDirectory();
            if (!Directory.Exists(outputDir))
            {
                return null;
            }

            var files = Directory.GetFiles(outputDir, Path.GetFileNameWithoutExtension(QuestBloodlessObbName) + "-*.obb");
            if (files.Length == 0)
            {
                return null;
            }

            Array.Sort(files, delegate (string left, string right)
            {
                return File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left));
            });

            return files[0];
        }

        private static string TryFindLatestQuestObbPatchReport()
        {
            var outputDir = GetQuestObbPatchedOutputDirectory();
            if (!Directory.Exists(outputDir))
            {
                return null;
            }

            var files = Directory.GetFiles(outputDir, "obb-patch-report-*.csv");
            if (files.Length == 0)
            {
                return null;
            }

            Array.Sort(files, delegate (string left, string right)
            {
                return File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left));
            });

            return files[0];
        }

        private static string GetQuestObbOutputDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AfterTheFallVRModKit", "quest-obb");
        }

        private static string GetQuestObbPatchedOutputDirectory()
        {
            return Path.Combine(GetQuestObbOutputDirectory(), "patched");
        }

        private static bool IsReadableQuestObb(string path)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(path))
                {
                    return archive.GetEntry("assets/bin/Data/541ba57ea63899e478c25da546f15ed9") != null &&
                        archive.GetEntry("assets/bin/Data/eccc90d64e804de4ba7eb24708909a7b") != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsQuestApkInstallLayoutReady(string apkPath)
        {
            if (GetZipEntryCompressionMethod(apkPath, "resources.arsc") != 0)
            {
                return false;
            }

            using (var archive = ZipFile.OpenRead(apkPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (IsNativeLibraryEntry(entry.FullName) && GetZipEntryCompressionMethod(apkPath, entry.FullName) != 0)
                    {
                        return false;
                    }
                }
            }

            try
            {
                var zipAlign = FindAndroidBuildTool("zipalign.exe");
                RunProcess(zipAlign, "-c -p 4 " + QuoteArgument(apkPath), 120000);
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static int GetZipEntryCompressionMethod(string zipPath, string entryName)
        {
            var fileInfo = new FileInfo(zipPath);
            if (!fileInfo.Exists)
            {
                return -1;
            }

            using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var eocdOffset = FindEndOfCentralDirectory(stream);
                if (eocdOffset < 0)
                {
                    return -1;
                }

                stream.Position = eocdOffset + 10;
                var entryCount = ReadUInt16(stream);
                stream.Position = eocdOffset + 16;
                var centralDirectoryOffset = ReadUInt32(stream);
                if (centralDirectoryOffset >= stream.Length)
                {
                    return -1;
                }

                stream.Position = centralDirectoryOffset;
                for (var i = 0; i < entryCount && stream.Position + 46 <= stream.Length; i++)
                {
                    if (ReadUInt32(stream) != 0x02014b50)
                    {
                        return -1;
                    }

                    stream.Position += 6;
                    var method = ReadUInt16(stream);
                    stream.Position += 16;
                    var nameLength = ReadUInt16(stream);
                    var extraLength = ReadUInt16(stream);
                    var commentLength = ReadUInt16(stream);
                    stream.Position += 12;

                    var nameBytes = new byte[nameLength];
                    if (stream.Read(nameBytes, 0, nameBytes.Length) != nameBytes.Length)
                    {
                        return -1;
                    }

                    var name = Encoding.UTF8.GetString(nameBytes).Replace('\\', '/');
                    if (string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase))
                    {
                        return method;
                    }

                    stream.Position += extraLength + commentLength;
                }
            }

            return -1;
        }

        private static long FindEndOfCentralDirectory(FileStream stream)
        {
            var maxSearch = (int)Math.Min(stream.Length, 65557);
            var buffer = new byte[maxSearch];
            stream.Position = stream.Length - maxSearch;
            if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                return -1;
            }

            for (var i = buffer.Length - 22; i >= 0; i--)
            {
                if (buffer[i] == 0x50 && buffer[i + 1] == 0x4b && buffer[i + 2] == 0x05 && buffer[i + 3] == 0x06)
                {
                    return stream.Length - maxSearch + i;
                }
            }

            return -1;
        }

        private static ushort ReadUInt16(Stream stream)
        {
            var b0 = stream.ReadByte();
            var b1 = stream.ReadByte();
            if (b0 < 0 || b1 < 0)
            {
                throw new EndOfStreamException();
            }

            return (ushort)(b0 | (b1 << 8));
        }

        private static uint ReadUInt32(Stream stream)
        {
            var b0 = stream.ReadByte();
            var b1 = stream.ReadByte();
            var b2 = stream.ReadByte();
            var b3 = stream.ReadByte();
            if (b0 < 0 || b1 < 0 || b2 < 0 || b3 < 0)
            {
                throw new EndOfStreamException();
            }

            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        private static void RestoreQuestObb(string adb, string backupDirectory)
        {
            RunAdbShell(adb, "mkdir -p " + QuoteShellArgument(QuestObbParentDirectory), 15000);
            if (RemoteDirectoryExists(adb, QuestObbDirectory))
            {
                RunAdbShell(adb, "rm -rf " + QuoteShellArgument(QuestObbDirectory), 120000);
            }

            RunAdbShell(adb, "mv " + QuoteShellArgument(backupDirectory) + " " + QuoteShellArgument(QuestObbDirectory), 120000);
        }

        private static bool IsSignatureMismatchInstallFailure(string output)
        {
            output = output ?? string.Empty;
            return output.IndexOf("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("signatures do not match", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("signatures don't match", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("different signature", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RemoteDirectoryExists(string adb, string remoteDirectory)
        {
            var output = RunAdbShell(adb, "if [ -d " + QuoteShellArgument(remoteDirectory) + " ]; then echo present; else echo missing; fi", 15000);
            return output.IndexOf("present", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RemoteFileExists(string adb, string remoteFile)
        {
            var output = RunAdbShell(adb, "if [ -f " + QuoteShellArgument(remoteFile) + " ]; then echo present; else echo missing; fi", 15000);
            return output.IndexOf("present", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureVerifiedQuestPackage(string adb, IProgress<string> progress)
        {
            if (progress != null)
            {
                progress.Report("Checking Quest package version...");
            }

            var details = RunProcess(adb, "shell dumpsys package " + QuestPackageName, 15000);
            var versionCode = MatchValue(details, @"versionCode=(?<value>\d+)");
            var versionName = MatchValue(details, @"versionName=(?<value>[^\r\n]+)");
            if (!string.Equals(versionCode, VerifiedQuestVersionCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Quest OBB patching is currently verified only for versionCode " + VerifiedQuestVersionCode +
                    " / versionName " + VerifiedQuestVersionName + ". Detected versionCode " +
                    EmptyIfNull(versionCode, "unknown") + " / versionName " +
                    EmptyIfNull(versionName, "unknown") + ".");
            }
        }

        private static long GetRemoteFileSize(string adb, string remoteFile)
        {
            var output = RunAdbShell(adb, "stat -c %s " + QuoteShellArgument(remoteFile), 15000);
            var match = Regex.Match(output ?? string.Empty, @"\d+");
            long size;
            if (match.Success && long.TryParse(match.Value, out size))
            {
                return size;
            }

            throw new InvalidOperationException("Could not read remote file size for " + remoteFile + ".");
        }

        private static long GetRemoteFileSizeOrZero(string adb, string remoteFile)
        {
            try
            {
                var output = RunAdbShell(adb, "if [ -f " + QuoteShellArgument(remoteFile) + " ]; then stat -c %s " + QuoteShellArgument(remoteFile) + "; else echo 0; fi", 15000);
                var match = Regex.Match(output ?? string.Empty, @"\d+");
                long size;
                if (match.Success && long.TryParse(match.Value, out size))
                {
                    return size;
                }
            }
            catch
            {
            }

            return 0;
        }

        private static void EnsureAdbDevice(string adb)
        {
            var devices = RunProcess(adb, "devices -l", 15000);
            if (devices.IndexOf("\tdevice", StringComparison.OrdinalIgnoreCase) < 0 && devices.IndexOf(" device ", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("No authorized ADB device was found. Connect the Quest and accept the USB debugging prompt.");
            }
        }

        private static string RunAdbShell(string adb, string command, int timeoutMilliseconds)
        {
            return RunProcess(adb, "shell " + QuoteArgument(command), timeoutMilliseconds);
        }

        private static void UpdateQuestObbEntry(ZipArchive archive, string entryName, Func<string, string> patch)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null)
            {
                throw new InvalidOperationException("OBB is missing " + entryName + ".");
            }

            var lastWriteTime = entry.LastWriteTime;
            var originalLength = entry.Length;
            byte[] originalBytes;
            using (var input = entry.Open())
            using (var memory = new MemoryStream())
            {
                input.CopyTo(memory);
                originalBytes = memory.ToArray();
            }

            var text = BytePreservingEncoding.GetString(originalBytes);
            var patchedText = patch(text);
            var patchedBytes = BytePreservingEncoding.GetBytes(patchedText);
            if (patchedBytes.Length != originalBytes.Length)
            {
                throw new InvalidOperationException(
                    "Patched length changed for " + entryName + ": " +
                    originalBytes.Length + " -> " + patchedBytes.Length + ".");
            }

            if (patchedBytes.Length != originalLength)
            {
                throw new InvalidOperationException("Unexpected length mismatch for " + entryName + ".");
            }

            entry.Delete();
            var newEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            newEntry.LastWriteTime = lastWriteTime;
            using (var output = newEntry.Open())
            {
                output.Write(patchedBytes, 0, patchedBytes.Length);
            }
        }

        private static string PatchQuestBloodSettingsText(string text, string entryName, List<QuestObbPatchReportRow> reportRows)
        {
            foreach (var field in new[]
            {
                "bulletMinDecalSize",
                "bulletMaxDecalSize",
                "bulletMinSizeDistance",
                "bulletMaxDistance",
                "indirectMinDecalSize",
                "indirectMaxDecalSize",
                "gibFloorPaintDelay",
                "maxGibSplatterRaycastDistance",
                "maxGibSplatterRandomVerticalAngle",
                "maxGibSplatterRandomHirozntalAngleFraction"
            })
            {
                text = SetQuestObbFieldNumber(text, entryName, field, "0.0", reportRows);
            }

            foreach (var scope in new[] { "minMaxGibFloorBloodSize", "gibSplatterPaintDelay", "minMaxGibSplatterBloodSize" })
            {
                text = SetQuestObbScopedNumber(text, entryName, scope, "x", "0.0", reportRows);
                text = SetQuestObbScopedNumber(text, entryName, scope, "y", "0.0", reportRows);
            }

            text = SetQuestObbScopedNumber(text, entryName, "gibSplatterRaycastMask", "value", "0", reportRows);
            foreach (var field in QuestBloodTextureArrayFields)
            {
                text = ClearQuestObbArrayField(text, entryName, field, reportRows);
            }

            return text;
        }

        private static string PatchQuestZombieDeathSettingsText(string text, string entryName, List<QuestObbPatchReportRow> reportRows)
        {
            foreach (var field in new[]
            {
                "bloodPoolMinSize",
                "bloodPoolMaxSize",
                "bloodPoolMinSpawnDuration",
                "bloodPoolMaxSpawnDuration"
            })
            {
                text = SetQuestObbFieldNumber(text, entryName, field, "0.0", reportRows);
            }

            return text;
        }

        private static string PatchQuestImpactSettingsText(string text, string entryName, List<QuestObbPatchReportRow> reportRows)
        {
            foreach (var field in new[]
            {
                "mutilationType",
                "MutilationType",
                "impactType",
                "ImpactType",
                "gibbingSettings",
                "GibbingSettings"
            })
            {
                text = SetQuestObbFieldNumber(text, entryName, field, "0", reportRows);
            }

            foreach (var field in new[]
            {
                "criticalHitChance",
                "CriticalHitChance"
            })
            {
                text = SetQuestObbFieldNumber(text, entryName, field, "0.0", reportRows);
            }

            return text;
        }

        private static string SetQuestObbFieldNumber(string text, string entryName, string fieldName, string newValue, List<QuestObbPatchReportRow> reportRows)
        {
            var count = 0;
            var regex = new Regex("(\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*)(-?\\d+(?:\\.\\d+)?)");
            var result = regex.Replace(text, delegate (Match match)
            {
                count++;
                return match.Groups[1].Value + NewPaddedQuestObbValue(match.Groups[2].Value, newValue);
            });

            reportRows.Add(new QuestObbPatchReportRow(entryName, fieldName, count));
            return result;
        }

        private static string SetQuestObbScopedNumber(string text, string entryName, string scopeFieldName, string valueFieldName, string newValue, List<QuestObbPatchReportRow> reportRows)
        {
            var scopeNeedle = "\"" + scopeFieldName + "\"";
            var regex = new Regex("(\"" + Regex.Escape(valueFieldName) + "\"\\s*:\\s*)(-?\\d+(?:\\.\\d+)?)");
            var position = 0;
            var count = 0;

            while (true)
            {
                var scopeIndex = text.IndexOf(scopeNeedle, position, StringComparison.Ordinal);
                if (scopeIndex < 0)
                {
                    break;
                }

                var segmentLength = Math.Min(700, text.Length - scopeIndex);
                var segment = text.Substring(scopeIndex, segmentLength);
                var match = regex.Match(segment);
                if (!match.Success)
                {
                    throw new InvalidOperationException("Could not find " + valueFieldName + " inside " + scopeFieldName + " in " + entryName + ".");
                }

                var oldValue = match.Groups[2].Value;
                var paddedValue = NewPaddedQuestObbValue(oldValue, newValue);
                var absoluteValueIndex = scopeIndex + match.Groups[2].Index;
                text = text.Remove(absoluteValueIndex, oldValue.Length).Insert(absoluteValueIndex, paddedValue);
                position = scopeIndex + scopeNeedle.Length;
                count++;
            }

            reportRows.Add(new QuestObbPatchReportRow(entryName, scopeFieldName + "." + valueFieldName, count));
            return text;
        }

        private static string ClearQuestObbArrayField(string text, string entryName, string fieldName, List<QuestObbPatchReportRow> reportRows)
        {
            var fieldNeedle = "\"" + fieldName + "\"";
            var position = 0;
            var count = 0;

            while (true)
            {
                var fieldIndex = text.IndexOf(fieldNeedle, position, StringComparison.Ordinal);
                if (fieldIndex < 0)
                {
                    break;
                }

                var colonIndex = text.IndexOf(':', fieldIndex + fieldNeedle.Length);
                if (colonIndex < 0)
                {
                    throw new InvalidOperationException("Could not find ':' after " + fieldName + " in " + entryName + ".");
                }

                var valueStart = GetQuestObbJsonValueStart(text, colonIndex);
                if (text[valueStart] == '[')
                {
                    text = ClearQuestObbJsonArrayAt(text, valueStart);
                    count++;
                }
                else if (text[valueStart] == '{')
                {
                    var objectEnd = FindQuestObbMatchingJsonDelimiter(text, valueStart, '{', '}');
                    var objectLength = objectEnd - valueStart + 1;
                    var objectText = text.Substring(valueStart, objectLength);

                    var countMatch = Regex.Match(objectText, "(\"\\$count\"\\s*:\\s*)(\\d+)");
                    if (countMatch.Success)
                    {
                        var oldCount = countMatch.Groups[2].Value;
                        var absoluteCountIndex = valueStart + countMatch.Groups[2].Index;
                        text = text.Remove(absoluteCountIndex, oldCount.Length).Insert(absoluteCountIndex, NewPaddedQuestObbValue(oldCount, "0"));
                    }

                    var valueNeedle = "\"$value\"";
                    var valueFieldIndex = text.IndexOf(valueNeedle, valueStart, objectLength, StringComparison.Ordinal);
                    if (valueFieldIndex < 0)
                    {
                        throw new InvalidOperationException("Could not find " + valueNeedle + " inside " + fieldName + " in " + entryName + ".");
                    }

                    var valueColonIndex = text.IndexOf(':', valueFieldIndex + valueNeedle.Length);
                    var arrayStart = GetQuestObbJsonValueStart(text, valueColonIndex);
                    if (text[arrayStart] != '[')
                    {
                        throw new InvalidOperationException("Expected " + fieldName + "." + valueNeedle + " to be an array in " + entryName + ".");
                    }

                    text = ClearQuestObbJsonArrayAt(text, arrayStart);
                    count++;
                }
                else
                {
                    throw new InvalidOperationException("Expected " + fieldName + " to be an array or typed array object in " + entryName + ".");
                }

                position = fieldIndex + fieldNeedle.Length;
            }

            reportRows.Add(new QuestObbPatchReportRow(entryName, fieldName + ".emptyArray", count));
            return text;
        }

        private static int GetQuestObbJsonValueStart(string text, int colonIndex)
        {
            var index = colonIndex + 1;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                throw new InvalidOperationException("Could not find value after JSON field.");
            }

            return index;
        }

        private static int FindQuestObbMatchingJsonDelimiter(string text, int openIndex, char openChar, char closeChar)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = openIndex; index < text.Length; index++)
            {
                var current = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                }
                else if (current == openChar)
                {
                    depth++;
                }
                else if (current == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                }
            }

            throw new InvalidOperationException("Could not find matching JSON delimiter " + closeChar + ".");
        }

        private static string ClearQuestObbJsonArrayAt(string text, int arrayStart)
        {
            var arrayEnd = FindQuestObbMatchingJsonDelimiter(text, arrayStart, '[', ']');
            var arrayLength = arrayEnd - arrayStart + 1;
            return SetPaddedQuestObbRange(text, arrayStart, arrayLength, "[]");
        }

        private static string SetPaddedQuestObbRange(string text, int start, int length, string newValue)
        {
            if (newValue.Length > length)
            {
                throw new InvalidOperationException("Replacement " + newValue + " is longer than original range length " + length + ".");
            }

            return text.Remove(start, length).Insert(start, newValue + new string(' ', length - newValue.Length));
        }

        private static string NewPaddedQuestObbValue(string oldValue, string newValue)
        {
            if (newValue.Length > oldValue.Length)
            {
                throw new InvalidOperationException("Replacement " + newValue + " is longer than original " + oldValue + ".");
            }

            return newValue + new string(' ', oldValue.Length - newValue.Length);
        }

        private static void WriteQuestObbPatchReport(string reportCsv, List<QuestObbPatchReportRow> reportRows)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Entry,Field,Count");
            foreach (var row in reportRows)
            {
                builder.Append(EscapeCsv(row.EntryName));
                builder.Append(',');
                builder.Append(EscapeCsv(row.FieldName));
                builder.Append(',');
                builder.AppendLine(row.Count.ToString());
            }

            File.WriteAllText(reportCsv, builder.ToString());
        }

        private static string InspectQuestApk(string apkPath)
        {
            var requiredEntries = new[]
            {
                QuestLibIl2CppEntryName,
                QuestUnityEntryName,
                QuestMetadataEntryName
            };

            var targetPatterns = new[]
            {
                "BloodPainter",
                "ServerGame",
                "ClientSceneManager",
                "FmodVoipRecorder",
                "VoipClient",
                "VoipRemotePeer",
                "ZombieMutilationView",
                "ZombieBloodMaskPainter",
                "ClientEnemyNetworking",
                "HandleEnemyGibNetworkMessage"
            };

            using (var archive = ZipFile.OpenRead(apkPath))
            {
                var builder = new StringBuilder();
                builder.AppendLine("APK inspection:");
                foreach (var entryName in requiredEntries)
                {
                    builder.AppendLine("- " + entryName + ": " + (archive.GetEntry(entryName) == null ? "missing" : "present"));
                }

                var metadataEntry = archive.GetEntry(QuestMetadataEntryName);
                if (metadataEntry == null)
                {
                    return builder.ToString();
                }

                string metadataText;
                using (var input = metadataEntry.Open())
                using (var memory = new MemoryStream())
                {
                    input.CopyTo(memory);
                    metadataText = Encoding.UTF8.GetString(memory.ToArray());
                }

                builder.AppendLine();
                builder.AppendLine("Known target metadata:");
                foreach (var pattern in targetPatterns)
                {
                    builder.AppendLine("- " + pattern + ": " + (metadataText.IndexOf(pattern, StringComparison.Ordinal) >= 0 ? "present" : "missing"));
                }

                return builder.ToString();
            }
        }

        private QuestApkPatchResult CreatePatchedQuestApk(string apkPath)
        {
            var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AfterTheFallVRModKit", "quest-apk", "patched");
            Directory.CreateDirectory(outputDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var workDir = Path.Combine(outputDir, "_work-" + stamp);
            Directory.CreateDirectory(workDir);

            var patchedLib = Path.Combine(workDir, "libil2cpp.so");
            var unsignedApk = Path.Combine(outputDir, QuestPackageName + "-modded-" + stamp + "-unsigned.apk");
            var alignedApk = Path.Combine(outputDir, QuestPackageName + "-modded-" + stamp + "-aligned.apk");
            var signedApk = Path.Combine(outputDir, QuestPackageName + "-modded-" + stamp + "-signed.apk");
            var reportCsv = Path.Combine(outputDir, "patch-report-" + stamp + ".csv");

            using (var archive = ZipFile.OpenRead(apkPath))
            {
                var libEntry = archive.GetEntry(QuestLibIl2CppEntryName);
                if (libEntry == null)
                {
                    throw new InvalidOperationException("APK is missing " + QuestLibIl2CppEntryName + ".");
                }

                if (archive.GetEntry(QuestMetadataEntryName) == null)
                {
                    throw new InvalidOperationException("APK is missing " + QuestMetadataEntryName + ".");
                }

                ExtractZipEntryToFile(libEntry, patchedLib);
            }

            var patchedCount = PatchQuestLibIl2Cpp(patchedLib, reportCsv);
            RebuildQuestApk(apkPath, patchedLib, unsignedApk);
            AlignAndSignQuestApk(unsignedApk, alignedApk, signedApk);

            return new QuestApkPatchResult
            {
                SignedApk = signedApk,
                ReportCsv = reportCsv,
                PatchedCount = patchedCount
            };
        }

        private static int PatchQuestLibIl2Cpp(string libPath, string reportCsv)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Category,Class,Method,Offset,OriginalBytes,PatchedBytes,Status");
            var patchedCount = 0;

            using (var stream = new FileStream(libPath, FileMode.Open, FileAccess.ReadWrite))
            {
                foreach (var target in QuestPatchTargets)
                {
                    if (target.Offset + Arm64ReturnInstruction.Length > stream.Length)
                    {
                        throw new InvalidOperationException("Patch offset " + FormatHexOffset(target.Offset) + " is outside libil2cpp.so.");
                    }

                    stream.Position = target.Offset;
                    var original = new byte[4];
                    if (stream.Read(original, 0, original.Length) != original.Length)
                    {
                        throw new EndOfStreamException("Could not read patch bytes at " + FormatHexOffset(target.Offset) + ".");
                    }

                    var alreadyPatched = BytesEqual(original, Arm64ReturnInstruction);
                    var matchesExpected = BytesEqual(original, target.ExpectedBytes);
                    if (!alreadyPatched && !matchesExpected)
                    {
                        throw new InvalidOperationException(
                            "Unexpected bytes at " + target.ClassName + "::" + target.MethodName +
                            " offset " + FormatHexOffset(target.Offset) + ". Expected " +
                            FormatBytes(target.ExpectedBytes) + ", found " + FormatBytes(original) +
                            ". This APK build is probably not the verified Quest build.");
                    }

                    if (!alreadyPatched)
                    {
                        stream.Position = target.Offset;
                        stream.Write(Arm64ReturnInstruction, 0, Arm64ReturnInstruction.Length);
                        patchedCount++;
                    }

                    builder.Append(EscapeCsv(target.Category));
                    builder.Append(',');
                    builder.Append(EscapeCsv(target.ClassName));
                    builder.Append(',');
                    builder.Append(EscapeCsv(target.MethodName));
                    builder.Append(',');
                    builder.Append(FormatHexOffset(target.Offset));
                    builder.Append(',');
                    builder.Append(EscapeCsv(FormatBytes(original)));
                    builder.Append(',');
                    builder.Append(EscapeCsv(FormatBytes(Arm64ReturnInstruction)));
                    builder.Append(',');
                    builder.AppendLine(alreadyPatched ? "already patched" : "patched");
                }
            }

            File.WriteAllText(reportCsv, builder.ToString());
            return patchedCount;
        }

        private static void RebuildQuestApk(string sourceApk, string patchedLib, string unsignedApk)
        {
            if (File.Exists(unsignedApk))
            {
                File.Delete(unsignedApk);
            }

            var repackDir = Path.Combine(Path.GetDirectoryName(unsignedApk), "_repack-" + Path.GetFileNameWithoutExtension(unsignedApk));
            if (Directory.Exists(repackDir))
            {
                Directory.Delete(repackDir, true);
            }

            Directory.CreateDirectory(repackDir);

            try
            {
                var repackRoot = Path.GetFullPath(repackDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                using (var source = ZipFile.OpenRead(sourceApk))
                {
                    foreach (var entry in source.Entries)
                    {
                        if (IsApkSignatureEntry(entry.FullName))
                        {
                            continue;
                        }

                        var target = Path.GetFullPath(Path.Combine(repackDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(repackRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("APK contains an invalid path: " + entry.FullName);
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var input = entry.Open())
                        using (var output = File.Create(target))
                        {
                            input.CopyTo(output);
                        }
                    }
                }

                var patchedLibTarget = Path.Combine(repackDir, QuestLibIl2CppEntryName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(patchedLibTarget));
                File.Copy(patchedLib, patchedLibTarget, true);

                var jar = FindJar();
                RunProcess(jar, "cf0M " + QuoteArgument(unsignedApk) + " -C " + QuoteArgument(repackDir) + " .", 300000);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(repackDir))
                    {
                        Directory.Delete(repackDir, true);
                    }
                }
                catch
                {
                    // Repack folders are temporary; a leftover folder is harmless and useful for debugging.
                }
            }
        }

        private static void AlignAndSignQuestApk(string unsignedApk, string alignedApk, string signedApk)
        {
            var zipAlign = FindAndroidBuildTool("zipalign.exe");
            var apkSignerJar = FindApkSignerJar();
            var java = FindJava();
            var keystore = EnsureQuestDebugKeystore();

            DeleteIfExists(alignedApk);
            DeleteIfExists(signedApk);

            RunProcess(zipAlign, "-p -f 4 " + QuoteArgument(unsignedApk) + " " + QuoteArgument(alignedApk), 120000);
            RunProcess(java,
                "-jar " + QuoteArgument(apkSignerJar) +
                " sign --ks " + QuoteArgument(keystore) +
                " --ks-key-alias afterthefallvrmodkit --ks-pass pass:android --key-pass pass:android --out " +
                QuoteArgument(signedApk) + " " + QuoteArgument(alignedApk),
                120000);
            RunProcess(java, "-jar " + QuoteArgument(apkSignerJar) + " verify --verbose " + QuoteArgument(signedApk), 120000);
        }

        private static string EnsureQuestDebugKeystore()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AfterTheFallVRModKit");
            Directory.CreateDirectory(root);
            var keystore = Path.Combine(root, "afterthefall-quest-debug.keystore");
            if (File.Exists(keystore))
            {
                return keystore;
            }

            var keytool = FindKeytool();
            RunProcess(keytool,
                "-genkeypair -v -keystore " + QuoteArgument(keystore) +
                " -storepass android -alias afterthefallvrmodkit -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname " +
                QuoteArgument("CN=After The Fall VR Mod Kit"),
                30000);
            return keystore;
        }

        private static string FindAndroidBuildTool(string fileName)
        {
            var sdkRoots = new List<string>();
            AddExistingDirectory(sdkRoots, Environment.GetEnvironmentVariable("ANDROID_HOME"));
            AddExistingDirectory(sdkRoots, Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
            AddExistingDirectory(sdkRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"));

            foreach (var sdkRoot in sdkRoots)
            {
                var buildToolsRoot = Path.Combine(sdkRoot, "build-tools");
                if (!Directory.Exists(buildToolsRoot))
                {
                    continue;
                }

                var directories = Directory.GetDirectories(buildToolsRoot);
                Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
                for (var i = directories.Length - 1; i >= 0; i--)
                {
                    var candidate = Path.Combine(directories[i], fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            throw new FileNotFoundException("Could not find Android build-tools " + fileName + ". Install Android SDK build-tools first.");
        }

        private static string FindApkSignerJar()
        {
            var sdkRoots = new List<string>();
            AddExistingDirectory(sdkRoots, Environment.GetEnvironmentVariable("ANDROID_HOME"));
            AddExistingDirectory(sdkRoots, Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
            AddExistingDirectory(sdkRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"));

            foreach (var sdkRoot in sdkRoots)
            {
                var buildToolsRoot = Path.Combine(sdkRoot, "build-tools");
                if (!Directory.Exists(buildToolsRoot))
                {
                    continue;
                }

                var directories = Directory.GetDirectories(buildToolsRoot);
                Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
                for (var i = directories.Length - 1; i >= 0; i--)
                {
                    var candidate = Path.Combine(directories[i], "lib", "apksigner.jar");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    candidate = Path.Combine(directories[i], "apksigner.jar");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            throw new FileNotFoundException("Could not find apksigner.jar. Install Android SDK build-tools first.");
        }

        private static string FindJava()
        {
            var candidates = new List<string>();
            AddPathToolCandidates(candidates, "java.exe");

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));
            }

            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"), "java.exe");
            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"), "java.exe");
            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Amazon Corretto"), "java.exe");

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Could not find java.exe. Install a JDK first.");
        }

        private static string FindJar()
        {
            var candidates = new List<string>();
            AddPathToolCandidates(candidates, "jar.exe");

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                candidates.Add(Path.Combine(javaHome, "bin", "jar.exe"));
            }

            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"), "jar.exe");
            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"), "jar.exe");
            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Amazon Corretto"), "jar.exe");

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Could not find jar.exe. Install a JDK first.");
        }

        private static string FindKeytool()
        {
            var candidates = new List<string>();
            AddPathToolCandidates(candidates, "keytool.exe");

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                candidates.Add(Path.Combine(javaHome, "bin", "keytool.exe"));
            }

            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"), "keytool.exe");
            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"), "keytool.exe");
            AddJavaToolCandidates(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Amazon Corretto"), "keytool.exe");

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Could not find keytool.exe. Install a JDK first.");
        }

        private static void AddJavaToolCandidates(List<string> candidates, string root, string fileName)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var direct = Path.Combine(root, "bin", fileName);
            if (File.Exists(direct))
            {
                candidates.Add(direct);
            }

            foreach (var directory in Directory.GetDirectories(root, "jdk*"))
            {
                candidates.Add(Path.Combine(directory, "bin", fileName));
            }
        }

        private static void AddPathToolCandidates(List<string> candidates, string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    candidates.Add(Path.Combine(directory.Trim(), fileName));
                }
            }
        }

        private static void AddExistingDirectory(List<string> paths, string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !paths.Contains(path))
            {
                paths.Add(path);
            }
        }

        private static bool IsApkSignatureEntry(string entryName)
        {
            if (!entryName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = Path.GetFileName(entryName);
            return string.Equals(fileName, "MANIFEST.MF", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".SF", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".EC", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNativeLibraryEntry(string entryName)
        {
            return entryName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
                entryName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldStoreQuestApkEntry(string entryName)
        {
            return IsNativeLibraryEntry(entryName) ||
                string.Equals(entryName, "resources.arsc", StringComparison.OrdinalIgnoreCase);
        }

        private static void ExtractZipEntryToFile(ZipArchiveEntry entry, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var input = entry.Open())
            using (var output = File.Create(path))
            {
                input.CopyTo(output);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string FormatBytes(byte[] bytes)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[i].ToString("X2"));
            }

            return builder.ToString();
        }

        private static string FormatHexOffset(long offset)
        {
            return "0x" + offset.ToString("X");
        }

        private static string FormatBinaryBytes(long bytes)
        {
            const double gib = 1024.0 * 1024.0 * 1024.0;
            const double mib = 1024.0 * 1024.0;

            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / gib).ToString("0.00") + " GiB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / mib).ToString("0.0") + " MiB";
            }

            return bytes + " B";
        }

        private static string FormatBinaryBytesPerSecond(double bytesPerSecond)
        {
            const double mib = 1024.0 * 1024.0;
            if (bytesPerSecond >= mib)
            {
                return (bytesPerSecond / mib).ToString("0.0") + " MiB/s";
            }

            return bytesPerSecond.ToString("0") + " B/s";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds < 1.0)
            {
                return "<1s";
            }

            if (duration.TotalMinutes < 1.0)
            {
                return Math.Ceiling(duration.TotalSeconds).ToString("0") + "s";
            }

            return ((int)duration.TotalMinutes).ToString("0") + "m " + duration.Seconds.ToString("00") + "s";
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string MatchValue(string text, string pattern)
        {
            var match = Regex.Match(text ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
        }

        private static string EmptyIfNull(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string TrimProcessOutput(ProcessRunResult result)
        {
            if (result == null)
            {
                return "(no output)";
            }

            var output = (result.CombinedOutput ?? string.Empty).Trim();
            return output.Length == 0 ? "(no output)" : output;
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteShellArgument(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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

        private static string FindAdb()
        {
            var candidates = new List<string>
            {
                "adb.exe",
                @"C:\adb\adb.exe"
            };

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    candidates.Add(Path.Combine(directory.Trim(), "adb.exe"));
                }
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    if (string.Equals(candidate, "adb.exe", StringComparison.OrdinalIgnoreCase) || File.Exists(candidate))
                    {
                        RunProcess(candidate, "version", 5000);
                        return candidate;
                    }
                }
                catch
                {
                    // Try the next common ADB location.
                }
            }

            throw new FileNotFoundException("ADB was not found. Install Android platform tools or place adb.exe at C:\\adb\\adb.exe.");
        }

        private static string RunProcess(string fileName, string arguments, int timeoutMilliseconds)
        {
            var result = RunProcessWithResult(fileName, arguments, timeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(fileName + " failed with exit code " + result.ExitCode + ": " + TrimProcessOutput(result));
            }

            return result.CombinedOutput;
        }

        private static string RunProcessWithLocalFileProgress(
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            string observedFile,
            long totalBytes,
            string actionName,
            IProgress<string> progress)
        {
            var result = RunProcessWithResultAndLocalFileProgress(
                fileName,
                arguments,
                timeoutMilliseconds,
                observedFile,
                totalBytes,
                actionName,
                progress);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(fileName + " failed with exit code " + result.ExitCode + ": " + TrimProcessOutput(result));
            }

            return result.CombinedOutput;
        }

        private static string RunProcessWithRemoteFileProgress(
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            string remoteFile,
            long totalBytes,
            string actionName,
            IProgress<string> progress)
        {
            var result = RunProcessWithResultAndRemoteFileProgress(
                fileName,
                arguments,
                timeoutMilliseconds,
                remoteFile,
                totalBytes,
                actionName,
                progress);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(fileName + " failed with exit code " + result.ExitCode + ": " + TrimProcessOutput(result));
            }

            return result.CombinedOutput;
        }

        private static ProcessRunResult RunProcessWithResult(string fileName, string arguments, int timeoutMilliseconds)
        {
            var start = CreateHiddenProcessStartInfo(fileName, arguments);

            using (var process = Process.Start(start))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Could not start " + fileName + ".");
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    throw new TimeoutException(fileName + " timed out.");
                }

                var output = outputTask.Result;
                var error = errorTask.Result;
                return new ProcessRunResult
                {
                    ExitCode = process.ExitCode,
                    Output = output,
                    Error = error
                };
            }
        }

        private static ProcessRunResult RunProcessWithResultAndRemoteFileProgress(
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            string remoteFile,
            long totalBytes,
            string actionName,
            IProgress<string> progress)
        {
            var start = CreateHiddenProcessStartInfo(fileName, arguments);
            using (var process = Process.Start(start))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Could not start " + fileName + ".");
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var started = DateTime.UtcNow;
                var deadline = started.AddMilliseconds(timeoutMilliseconds);
                var lastReport = DateTime.MinValue;
                var lastPercent = -1;

                while (!process.WaitForExit(1000))
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        throw new TimeoutException(fileName + " timed out.");
                    }

                    ReportRemoteFileTransferProgress(fileName, remoteFile, totalBytes, actionName, started, progress, ref lastReport, ref lastPercent);
                }

                ReportRemoteFileTransferProgress(fileName, remoteFile, totalBytes, actionName, started, progress, ref lastReport, ref lastPercent);

                var output = outputTask.Result;
                var error = errorTask.Result;
                return new ProcessRunResult
                {
                    ExitCode = process.ExitCode,
                    Output = output,
                    Error = error
                };
            }
        }

        private static ProcessRunResult RunProcessWithResultAndLocalFileProgress(
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            string observedFile,
            long totalBytes,
            string actionName,
            IProgress<string> progress)
        {
            var start = CreateHiddenProcessStartInfo(fileName, arguments);
            using (var process = Process.Start(start))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Could not start " + fileName + ".");
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var started = DateTime.UtcNow;
                var deadline = started.AddMilliseconds(timeoutMilliseconds);
                var lastReport = DateTime.MinValue;
                var lastPercent = -1;

                while (!process.WaitForExit(500))
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        throw new TimeoutException(fileName + " timed out.");
                    }

                    ReportLocalFileTransferProgress(observedFile, totalBytes, actionName, started, progress, ref lastReport, ref lastPercent);
                }

                ReportLocalFileTransferProgress(observedFile, totalBytes, actionName, started, progress, ref lastReport, ref lastPercent);

                var output = outputTask.Result;
                var error = errorTask.Result;
                return new ProcessRunResult
                {
                    ExitCode = process.ExitCode,
                    Output = output,
                    Error = error
                };
            }
        }

        private static ProcessStartInfo CreateHiddenProcessStartInfo(string fileName, string arguments)
        {
            var start = new ProcessStartInfo();
            if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                start.Arguments = "/c " + QuoteArgument(fileName) + " " + arguments;
            }
            else
            {
                start.FileName = fileName;
                start.Arguments = arguments;
            }

            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.CreateNoWindow = true;
            return start;
        }

        private static void ReportLocalFileTransferProgress(
            string observedFile,
            long totalBytes,
            string actionName,
            DateTime started,
            IProgress<string> progress,
            ref DateTime lastReport,
            ref int lastPercent)
        {
            if (progress == null || totalBytes <= 0 || !File.Exists(observedFile))
            {
                return;
            }

            var bytes = new FileInfo(observedFile).Length;
            ReportMeasuredTransferProgress(bytes, totalBytes, actionName, started, progress, ref lastReport, ref lastPercent);
        }

        private static void ReportRemoteFileTransferProgress(
            string adb,
            string remoteFile,
            long totalBytes,
            string actionName,
            DateTime started,
            IProgress<string> progress,
            ref DateTime lastReport,
            ref int lastPercent)
        {
            if (progress == null || totalBytes <= 0)
            {
                return;
            }

            var bytes = GetRemoteFileSizeOrZero(adb, remoteFile);
            if (bytes <= 0)
            {
                return;
            }

            ReportMeasuredTransferProgress(bytes, totalBytes, actionName, started, progress, ref lastReport, ref lastPercent);
        }

        private static void ReportMeasuredTransferProgress(
            long bytes,
            long totalBytes,
            string actionName,
            DateTime started,
            IProgress<string> progress,
            ref DateTime lastReport,
            ref int lastPercent)
        {
            var now = DateTime.UtcNow;
            var percent = (int)Math.Max(0, Math.Min(100, Math.Round((bytes * 100.0) / totalBytes)));
            if (percent == lastPercent && (now - lastReport).TotalSeconds < 2.0)
            {
                return;
            }

            lastPercent = percent;
            lastReport = now;

            var elapsedSeconds = Math.Max(0.1, (now - started).TotalSeconds);
            var bytesPerSecond = bytes / elapsedSeconds;
            var remainingSeconds = bytesPerSecond > 0.0 ? Math.Max(0.0, (totalBytes - bytes) / bytesPerSecond) : 0.0;
            progress.Report(
                actionName + "... " + percent + "% (" +
                FormatBinaryBytes(bytes) + " / " + FormatBinaryBytes(totalBytes) + ", " +
                FormatBinaryBytesPerSecond(bytesPerSecond) + ", ETA " +
                FormatDuration(TimeSpan.FromSeconds(remainingSeconds)) + ")");
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
            foreach (var root in GetSteamInstallRoots())
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

        private static IEnumerable<string> GetSteamInstallRoots()
        {
            var roots = new List<string>();
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "InstallPath", null) as string);
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
            AddSteamRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string);
            return roots;
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
            public bool DoorbellWaveSound;
            public bool ComfortEnemyVisuals;
            public bool CleanupRetainedServerGame;

            public static FeatureConfig Defaults()
            {
                return new FeatureConfig
                {
                    DisableInGameVoip = true,
                    SuppressClientBloodAndGore = true,
                    DoorbellWaveSound = true,
                    ComfortEnemyVisuals = false,
                    CleanupRetainedServerGame = true
                };
            }
        }

        private sealed class SteamLaunchOptions
        {
            public bool Found;
            public string LocalConfigPath;
            public string Options;
        }

        private sealed class QuestApkPullResult
        {
            public string LocalApk;
            public string VersionCode;
            public string VersionName;
            public string Report;
        }

        private sealed class QuestApkPatchResult
        {
            public string SignedApk;
            public string ReportCsv;
            public int PatchedCount;
        }

        private sealed class QuestObbPatchResult
        {
            public string PatchedObb;
            public string ReportCsv;
            public int PatchedFieldCount;
            public bool UsedCachedPatchedObb;
        }

        private sealed class QuestObbPatchReportRow
        {
            public readonly string EntryName;
            public readonly string FieldName;
            public readonly int Count;

            public QuestObbPatchReportRow(string entryName, string fieldName, int count)
            {
                EntryName = entryName;
                FieldName = fieldName;
                Count = count;
            }
        }

        private sealed class ProcessRunResult
        {
            public int ExitCode;
            public string Output;
            public string Error;

            public string CombinedOutput
            {
                get
                {
                    return string.IsNullOrEmpty(Error) ? (Output ?? string.Empty) : (Output ?? string.Empty) + Environment.NewLine + Error;
                }
            }
        }

        private sealed class QuestPatchTarget
        {
            public readonly string Category;
            public readonly string ClassName;
            public readonly string MethodName;
            public readonly long Offset;
            public readonly byte[] ExpectedBytes;

            public QuestPatchTarget(string category, string className, string methodName, long offset, byte[] expectedBytes)
            {
                Category = category;
                ClassName = className;
                MethodName = methodName;
                Offset = offset;
                ExpectedBytes = expectedBytes;
            }
        }
    }
}
