using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SherpaOnnxConfig
{
    public partial class MainForm : Form
    {
        private Label? titleLabel;
        private Label? statusLabel;
        private ComboBox? languageComboBox;
        private CheckBox? downloadedOnlyCheckBox;
        private ComboBox? voiceComboBox;
        private Button? downloadButton;
        private Button? testVoiceButton;
        private Button? openModelsFolderButton;
        private Button? rescanModelsButton;
        private Button? installForAdminAppsButton;
        private CheckBox? enUsCompatAliasCheckBox;
        private RichTextBox? outputTextBox;
        private TextBox? testTextInput;
        private ProgressBar? progressBar;
        private Label? downloadProgressLabel;
        private BackgroundWorker? downloadWorker;
        private GroupBox? voiceGroup;
        private GroupBox? testGroup;
        private GroupBox? actionsGroup;
        private Label? modelInfoLabel;
        private Label? testHintLabel;
        private Label? actionsHintLabel;

        private SherpaModelsCatalog? sherpaCatalog = null;
        private static readonly string AdapterDataDir = AppDataLayout.AdapterDataDir;
        private static readonly string ModelsDir = AppDataLayout.ModelsDir;
        private static readonly string ScanErrorsPath = Path.Combine(AdapterDataDir, "sherpa_model_scan_errors.json");
        private const string SapiTokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
        private const string EnumeratorConfigKeyPath = @"Software\VoiceGardenSAPIAdapter\Enumerator";
        private const string TtsEngineClsid = "{013ab33b-ad1a-401c-8bee-f6e2b046a94e}";
        private const string VoiceEnumClsid = "{b8b9e38f-e5a2-4661-9fde-4ac7377aa6f6}";
        private const string TokenEnumKeyPath = @"SOFTWARE\Microsoft\Speech\Voices\TokenEnums\VoiceGardenEnumerator";
        private const string SherpaCompatKeyPath = @"Software\VoiceGardenSAPIAdapter\SherpaCompat";
        private const string AllLanguagesOption = "All Languages";
        private const string CompatibilityAliasSuffix = "-enUS";
        private bool suppressComboEvents;
        private bool suppressCompatAliasEvents;

        // Voice list for CLI access
        public static List<VoiceInfo> AllVoices { get; private set; } = new List<VoiceInfo>();

        private readonly bool autoRescanOnStartup;
        private static List<ModelScanIssue> s_lastScanIssues = new List<ModelScanIssue>();
        private string? activeDownloadModelDir;
        private string? activeDownloadArchive;

        public MainForm(bool autoRescanOnStartup = false)
        {
            this.autoRescanOnStartup = autoRescanOnStartup;
            InitializeComponent();
            LoadCatalogsAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "VoiceGarden SAPI - SherpaOnnx Model Manager";
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Size = new Size(1080, 920);
            this.MinimumSize = new Size(980, 820);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.BackColor = Color.FromArgb(245, 245, 245);

            // Title
            titleLabel = new Label
            {
                Location = new Point(20, 15),
                Size = new Size(920, 34),
                Text = "SherpaOnnx Offline TTS Model Manager",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 51, 102)
            };
            titleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Status label
            statusLabel = new Label
            {
                Location = new Point(20, 54),
                Size = new Size(920, 26),
                Text = "Status: Loading voice catalog...",
                ForeColor = Color.FromArgb(100, 100, 100)
            };
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Language selection
            Label languageLabel = new Label
            {
                Location = new Point(20, 92),
                Size = new Size(120, 28),
                Text = "Language:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            languageComboBox = new ComboBox
            {
                Location = new Point(140, 90),
                Size = new Size(240, 30),
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.None,
                AutoCompleteSource = AutoCompleteSource.None,
                Font = new Font("Segoe UI", 9F)
            };
            languageComboBox.Items.Add(AllLanguagesOption);
            languageComboBox.SelectedIndex = 0;
            languageComboBox.SelectedIndexChanged += LanguageComboBox_SelectedIndexChanged;
            languageComboBox.TextUpdate += LanguageComboBox_TextUpdate;
            languageComboBox.KeyDown += LanguageComboBox_KeyDown;
            languageComboBox.Leave += LanguageComboBox_Leave;

            downloadedOnlyCheckBox = new CheckBox
            {
                Location = new Point(400, 92),
                Size = new Size(170, 24),
                Text = "Downloaded Only",
                Checked = false
            };
            downloadedOnlyCheckBox.CheckedChanged += (_, _) =>
            {
                string lang = languageComboBox?.SelectedItem?.ToString() ?? AllLanguagesOption;
                // Toggling this is a scope change, not a text-search action.
                UpdateVoiceList(lang, null);
            };

            // Voice selection
            voiceGroup = new GroupBox
            {
                Location = new Point(20, 135),
                Size = new Size(920, 160),
                Text = "Available SherpaOnnx Models"
            };
            voiceGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label voiceLabel = new Label
            {
                Location = new Point(15, 32),
                Size = new Size(80, 26),
                Text = "Model:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            voiceComboBox = new ComboBox
            {
                Location = new Point(100, 30),
                Size = new Size(810, 30),
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.None,
                AutoCompleteSource = AutoCompleteSource.None,
                Font = new Font("Segoe UI", 9F)
            };
            voiceComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            voiceComboBox.SelectedIndexChanged += VoiceComboBox_SelectedIndexChanged;
            voiceComboBox.TextUpdate += VoiceComboBox_TextUpdate;

            downloadButton = new Button
            {
                Location = new Point(15, 72),
                Size = new Size(150, 34),
                Text = "Download Model",
                BackColor = Color.FromArgb(255, 140, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            downloadButton.FlatAppearance.BorderSize = 0;
            downloadButton.Click += DownloadButton_Click;


            modelInfoLabel = new Label
            {
                Location = new Point(15, 114),
                Size = new Size(895, 35),
                Text = $"Select a language and model to download. Models are cached in {ModelsDir}\\",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };
            modelInfoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            progressBar = new ProgressBar
            {
                Location = new Point(300, 74),
                Size = new Size(485, 22),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            downloadProgressLabel = new Label
            {
                Location = new Point(300, 100),
                Size = new Size(610, 18),
                Text = "",
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 8F),
                Visible = false
            };
            downloadProgressLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            voiceGroup.Controls.Add(voiceLabel);
            voiceGroup.Controls.Add(voiceComboBox);
            voiceGroup.Controls.Add(downloadButton);
            voiceGroup.Controls.Add(modelInfoLabel);
            voiceGroup.Controls.Add(progressBar);
            voiceGroup.Controls.Add(downloadProgressLabel);

            // Test group
            testGroup = new GroupBox
            {
                Location = new Point(20, 305),
                Size = new Size(920, 130),
                Text = "Test Voice (After Download)"
            };
            testGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            testTextInput = new TextBox
            {
                Location = new Point(15, 34),
                Size = new Size(745, 30),
                Text = "The quick brown fox jumps over the lazy dog.",
                Font = new Font("Segoe UI", 9F)
            };
            testTextInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            testVoiceButton = new Button
            {
                Location = new Point(770, 32),
                Size = new Size(140, 34),
                Text = "▶ Test",
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            testVoiceButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            testVoiceButton.FlatAppearance.BorderSize = 0;
            testVoiceButton.Click += TestVoiceButton_Click;

            testHintLabel = new Label
            {
                Location = new Point(15, 70),
                Size = new Size(895, 45),
                Text = "Tests the selected voice using SAPI5. The voice must be downloaded and the DLL registered first.",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };
            testHintLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            testGroup.Controls.Add(testTextInput);
            testGroup.Controls.Add(testVoiceButton);
            testGroup.Controls.Add(testHintLabel);

            // Actions group
            actionsGroup = new GroupBox
            {
                Location = new Point(20, 445),
                Size = new Size(920, 100),
                Text = "Actions"
            };
            actionsGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            openModelsFolderButton = new Button
            {
                Location = new Point(15, 30),
                Size = new Size(190, 36),
                Text = "Open Models Folder",
                FlatStyle = FlatStyle.Flat
            };
            openModelsFolderButton.Click += OpenModelsFolderButton_Click;

            rescanModelsButton = new Button
            {
                Location = new Point(215, 30),
                Size = new Size(170, 36),
                Text = "Rescan Models",
                FlatStyle = FlatStyle.Flat
            };
            rescanModelsButton.Click += RescanModelsButton_Click;

            installForAdminAppsButton = new Button
            {
                Location = new Point(395, 30),
                Size = new Size(220, 36),
                Text = "Install for Admin Apps",
                FlatStyle = FlatStyle.Flat
            };
            installForAdminAppsButton.Click += InstallForAdminAppsButton_Click;

            enUsCompatAliasCheckBox = new CheckBox
            {
                Location = new Point(625, 36),
                Size = new Size(285, 24),
                Text = "Add en-US compatibility alias",
                Checked = false,
                Enabled = false
            };
            enUsCompatAliasCheckBox.CheckedChanged += EnUsCompatAliasCheckBox_CheckedChanged;

            actionsHintLabel = new Label
            {
                Location = new Point(15, 72),
                Size = new Size(895, 22),
                Text = "Rescan validates models. Compatibility alias adds a second en-US token for apps that hide non-en-US voices.",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };
            actionsHintLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            actionsGroup.Controls.Add(openModelsFolderButton);
            actionsGroup.Controls.Add(rescanModelsButton);
            actionsGroup.Controls.Add(installForAdminAppsButton);
            actionsGroup.Controls.Add(enUsCompatAliasCheckBox);
            actionsGroup.Controls.Add(actionsHintLabel);

            // Output
            outputTextBox = new RichTextBox
            {
                Location = new Point(20, 555),
                Size = new Size(920, 295),
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle,
                Text = "Welcome to SherpaOnnx Model Manager!\r\n\r\n" +
                       "Loading voice catalog...\r\n\r\n" +
                       "CLI Usage:\r\n" +
                       "  SherpaOnnxConfig.exe list\r\n" +
                       "  SherpaOnnxConfig.exe list --language \"English\"\r\n" +
                       "  SherpaOnnxConfig.exe download <model-id>\r\n" +
                       "  SherpaOnnxConfig.exe downloaded\r\n" +
                       "  SherpaOnnxConfig.exe rescan\r\n\r\n"
            };
            outputTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Background worker for downloads
            downloadWorker = new BackgroundWorker();
            downloadWorker.WorkerReportsProgress = true;
            downloadWorker.WorkerSupportsCancellation = true;
            downloadWorker.DoWork += DownloadWorker_DoWork;
            downloadWorker.ProgressChanged += DownloadWorker_ProgressChanged;
            downloadWorker.RunWorkerCompleted += DownloadWorker_RunWorkerCompleted;

            this.Controls.Add(titleLabel);
            this.Controls.Add(statusLabel);
            this.Controls.Add(languageLabel);
            this.Controls.Add(languageComboBox);
            this.Controls.Add(downloadedOnlyCheckBox);
            this.Controls.Add(voiceGroup);
            this.Controls.Add(testGroup);
            this.Controls.Add(actionsGroup);
            this.Controls.Add(outputTextBox);
            this.Resize += (_, _) => ApplyResponsiveLayout();

            this.Shown += (_, _) =>
            {
                ApplyResponsiveLayout();
                if (autoRescanOnStartup)
                {
                    PerformLocalModelRescan();
                }
            };
        }

        private HashSet<string> allLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<VoiceInfo>> voicesByLanguage = new Dictionary<string, List<VoiceInfo>>(StringComparer.OrdinalIgnoreCase);
        private List<string> sortedLanguages = new List<string>();

        private void ApplyResponsiveLayout()
        {
            if (titleLabel == null || statusLabel == null || languageComboBox == null || voiceGroup == null ||
                testGroup == null || actionsGroup == null || outputTextBox == null || testVoiceButton == null ||
                testTextInput == null || voiceComboBox == null || progressBar == null || downloadProgressLabel == null ||
                modelInfoLabel == null || testHintLabel == null || actionsHintLabel == null ||
                openModelsFolderButton == null || rescanModelsButton == null || installForAdminAppsButton == null ||
                downloadedOnlyCheckBox == null || enUsCompatAliasCheckBox == null)
            {
                return;
            }

            const int margin = 20;
            int width = Math.Max(720, this.ClientSize.Width - (margin * 2));

            titleLabel.SetBounds(margin, 15, width, 34);
            statusLabel.SetBounds(margin, 54, width, 24);
            languageComboBox.SetBounds(140, 90, Math.Min(320, width - 180), 30);
            downloadedOnlyCheckBox.SetBounds(languageComboBox.Right + 12, 94, 170, 24);

            voiceGroup.SetBounds(margin, 135, width, 168);
            int voiceInner = Math.Max(420, voiceGroup.ClientSize.Width - 30);
            voiceComboBox.SetBounds(100, 30, Math.Max(260, voiceInner - 85), 30);
            downloadButton.SetBounds(15, 72, 150, 34);
            progressBar.SetBounds(200, 74, Math.Max(200, voiceInner - 215), 22);
            downloadProgressLabel.SetBounds(200, 100, Math.Max(220, voiceInner - 215), 18);
            modelInfoLabel.SetBounds(15, 120, Math.Max(260, voiceInner), 35);

            testGroup.SetBounds(margin, 313, width, 130);
            int testInner = Math.Max(420, testGroup.ClientSize.Width - 30);
            int buttonWidth = 140;
            int buttonLeft = 15 + testInner - buttonWidth;
            testVoiceButton.SetBounds(buttonLeft, 32, buttonWidth, 34);
            testTextInput.SetBounds(15, 34, Math.Max(240, buttonLeft - 25), 30);
            testHintLabel.SetBounds(15, 70, Math.Max(260, testInner), 45);

            actionsGroup.SetBounds(margin, 451, width, 100);
            int actionsInner = Math.Max(420, actionsGroup.ClientSize.Width - 30);
            openModelsFolderButton.SetBounds(15, 30, 190, 36);
            rescanModelsButton.SetBounds(215, 30, 170, 36);
            installForAdminAppsButton.SetBounds(395, 30, 220, 36);
            enUsCompatAliasCheckBox.SetBounds(625, 36, Math.Max(240, actionsInner - 610), 24);
            actionsHintLabel.SetBounds(15, 72, Math.Max(260, actionsInner), 22);

            int outputTop = actionsGroup.Bottom + 10;
            int outputHeight = Math.Max(180, this.ClientSize.Height - outputTop - margin);
            outputTextBox.SetBounds(margin, outputTop, width, outputHeight);
        }

        private async void LoadCatalogsAsync()
        {
            try
            {
                statusLabel!.Text = "Status: Loading SherpaOnnx catalog...";

                // Try to find catalog in multiple locations
                string[] catalogPaths = new string[]
                {
                    Path.Combine(AppContext.BaseDirectory, "models.json"),
                    Path.Combine(AppContext.BaseDirectory, "sherpa-config", "models.json"),
                    Path.Combine(Application.StartupPath, "models.json"),
                    Path.Combine(Application.StartupPath, "sherpa-config", "models.json"),
                    "models.json"
                };

                string? catalogContent = null;
                foreach (string catalogPath in catalogPaths)
                {
                    if (File.Exists(catalogPath))
                    {
                        catalogContent = await File.ReadAllTextAsync(catalogPath);
                        AppendOutput($"Loaded catalog from: {catalogPath}", Color.FromArgb(100, 255, 100));
                        break;
                    }
                }

                if (string.IsNullOrEmpty(catalogContent))
                {
                    AppendOutput("WARNING: models.json not found. Models will need to be added manually.", Color.FromArgb(255, 200, 100));
                    statusLabel!.Text = "Status: No catalog found";
                    languageComboBox!.Items.Add(AllLanguagesOption);
                    languageComboBox.SelectedIndex = 0;
                    return;
                }

                sherpaCatalog = JsonSerializer.Deserialize<SherpaModelsCatalog>(catalogContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                AppendOutput($"Loaded {sherpaCatalog?.Count ?? 0} SherpaOnnx models from catalog.", Color.FromArgb(100, 255, 100));

                // Process models into language groups
                AllVoices.Clear();
                voicesByLanguage.Clear();
                allLanguages.Clear();
                allLanguages.Add(AllLanguagesOption);

                if (sherpaCatalog != null)
                {
                    int skippedCount = 0;
                    int processedCount = 0;

                    foreach (var kvp in sherpaCatalog)
                    {
                        try
                        {
                            var model = kvp.Value;
                            var languageNames = GetLanguageDisplayNames(model).ToList();
                            if (languageNames.Count == 0)
                            {
                                skippedCount++;
                                continue;
                            }

                            // Create voice info
                            string engineType = model.id?.Contains("mms") == true ? "MMS" :
                                              model.id?.Contains("kokoro") == true ? "Kokoro" :
                                              model.id?.Contains("vits") == true ? "VITS" :
                                              model.id?.Contains("matcha") == true ? "Matcha-TTS" : "SherpaOnnx";

                            var voice = new VoiceInfo
                            {
                                Id = model.id ?? kvp.Key,
                                Name = string.IsNullOrWhiteSpace(model.name) ? (model.id ?? kvp.Key) : model.name,
                                Language = string.Join(", ", languageNames),
                                EngineType = engineType,
                                IsOffline = true,
                                ModelUrl = model.url ?? model.url_ ?? "",
                                ModelSize = model.filesize_mb ?? 0,
                                SampleRate = model.sample_rate ?? 22050,
                                ModelType = model.model_type ?? "vits",
                                Source = "sherpa"
                            };

                            AllVoices.Add(voice);

                            // Group by each declared language so multilingual models are discoverable.
                            foreach (var languageName in languageNames)
                            {
                                allLanguages.Add(languageName);
                                if (!voicesByLanguage.ContainsKey(languageName))
                                    voicesByLanguage[languageName] = new List<VoiceInfo>();
                                voicesByLanguage[languageName].Add(voice);
                            }

                            processedCount++;
                        }
                        catch (Exception ex)
                        {
                            AppendOutput($"Skipping model {kvp.Key}: {ex.Message}", Color.FromArgb(255, 150, 100));
                        }
                    }

                    if (skippedCount > 0)
                    {
                        AppendOutput($"Skipped {skippedCount} models (no valid language)", Color.FromArgb(255, 200, 100));
                    }
                    AppendOutput($"Successfully processed {processedCount} models", Color.FromArgb(100, 255, 100));
                }

                // Populate language dropdown
                sortedLanguages = allLanguages.OrderBy(l => l).ToList();
                RefreshLanguageItems(null, AllLanguagesOption);

                // Show all voices initially
                UpdateVoiceList(AllLanguagesOption);

                AppendOutput($"Found {allLanguages.Count - 1} unique languages with {AllVoices.Count} models.", Color.FromArgb(100, 200, 255));
                TrySyncPersistentTokens("startup");

                statusLabel!.Text = $"Status: Ready - {AllVoices.Count} models available";
                ShowLastScanIssuesSummary();
            }
            catch (Exception ex)
            {
                AppendOutput($"Error loading catalog: {ex.Message}", Color.FromArgb(255, 100, 100));
                statusLabel!.Text = "Status: Error loading catalog";
            }
        }

        private void LanguageComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (suppressComboEvents)
                return;

            if (languageComboBox!.SelectedItem != null)
            {
                UpdateVoiceList(languageComboBox.SelectedItem.ToString() ?? AllLanguagesOption);
            }
        }

        private void LanguageComboBox_TextUpdate(object? sender, EventArgs e)
        {
            if (suppressComboEvents || languageComboBox == null)
                return;

            string query = languageComboBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                RefreshLanguageItems(null, languageComboBox.SelectedItem?.ToString());
                return;
            }

            // Defer refresh to avoid mutating ComboBox items during TextUpdate.
            BeginInvoke((Action)(() =>
            {
                if (languageComboBox == null || languageComboBox.IsDisposed)
                    return;

                string latest = languageComboBox.Text?.Trim() ?? string.Empty;
                RefreshLanguageItems(latest, null);
                languageComboBox.Text = latest;
                languageComboBox.SelectionStart = languageComboBox.Text.Length;
                languageComboBox.SelectionLength = 0;
                if (languageComboBox.Items.Count > 0)
                    languageComboBox.DroppedDown = true;
            }));
        }

        private void LanguageComboBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                TrySelectLanguageFromText();
                e.SuppressKeyPress = true;
            }
        }

        private void LanguageComboBox_Leave(object? sender, EventArgs e)
        {
            TrySelectLanguageFromText();
        }

        private void TrySelectLanguageFromText()
        {
            if (languageComboBox == null)
                return;

            string query = languageComboBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                if (languageComboBox.SelectedItem == null)
                    languageComboBox.SelectedItem = AllLanguagesOption;
                return;
            }

            string? exact = allLanguages.FirstOrDefault(l => l.Equals(query, StringComparison.OrdinalIgnoreCase));
            string? partial = allLanguages.FirstOrDefault(l => l.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            string? choice = exact ?? partial;

            if (string.IsNullOrWhiteSpace(choice))
            {
                statusLabel!.Text = $"Status: No language match for '{query}'";
                return;
            }

            suppressComboEvents = true;
            try
            {
                languageComboBox.SelectedItem = choice;
                languageComboBox.Text = choice;
                languageComboBox.SelectionStart = languageComboBox.Text.Length;
            }
            finally
            {
                suppressComboEvents = false;
            }

            UpdateVoiceList(choice);
        }

        private void RefreshLanguageItems(string? query, string? preferredSelection)
        {
            if (languageComboBox == null)
                return;

            IEnumerable<string> source = sortedLanguages;
            if (!string.IsNullOrWhiteSpace(query))
            {
                source = source.Where(l => l.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            suppressComboEvents = true;
            languageComboBox.BeginUpdate();
            try
            {
                languageComboBox.Items.Clear();
                foreach (string language in source)
                {
                    languageComboBox.Items.Add(language);
                }

                if (languageComboBox.Items.Count == 0)
                {
                    languageComboBox.Items.Add(AllLanguagesOption);
                }

                bool inTypingMode = !string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(preferredSelection);
                if (!inTypingMode)
                {
                    string target = preferredSelection ?? languageComboBox.SelectedItem?.ToString() ?? AllLanguagesOption;
                    int idx = languageComboBox.Items.IndexOf(target);
                    languageComboBox.SelectedIndex = idx >= 0 ? idx : 0;
                }
            }
            finally
            {
                languageComboBox.EndUpdate();
                suppressComboEvents = false;
            }
        }

        private void UpdateVoiceList(string language)
        {
            UpdateVoiceList(language, null, null);
        }

        private void UpdateVoiceList(string language, string? voiceFilter)
        {
            UpdateVoiceList(language, voiceFilter, null);
        }

        private void UpdateVoiceList(string language, string? voiceFilter, string? preferredVoiceId)
        {
            string? previouslySelectedId = preferredVoiceId;
            if (string.IsNullOrWhiteSpace(previouslySelectedId))
            {
                var current = GetSelectedVoice();
                previouslySelectedId = current?.Id;
            }

            voiceComboBox!.Items.Clear();
            voiceComboBox.SelectedIndex = -1;

            IEnumerable<VoiceInfo> voicesToShow;

            if (language == AllLanguagesOption)
            {
                voicesToShow = AllVoices;
            }
            else if (voicesByLanguage.ContainsKey(language))
            {
                voicesToShow = voicesByLanguage[language];
            }
            else
            {
                voicesToShow = Enumerable.Empty<VoiceInfo>();
            }

            string filter = voiceFilter?.Trim() ?? string.Empty;
            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            if (hasFilter)
            {
                voicesToShow = voicesToShow.Where(v =>
                    v.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    v.Language.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            if (downloadedOnlyCheckBox?.Checked == true)
            {
                voicesToShow = voicesToShow.Where(v => IsModelDownloaded(v.Id));
            }

            suppressComboEvents = true;
            int preferredIndex = -1;
            int index = 0;
            foreach (var voice in voicesToShow.OrderBy(v => v.Name))
            {
                bool hasLocalDir = HasModelDirectory(voice.Id);
                bool downloaded = IsModelDownloaded(voice.Id);
                string status = downloaded ? "[✓]" : (hasLocalDir ? "[!]" : "[↓]");
                string size = voice.ModelSize > 0 ? $" ({voice.ModelSize:F0} MB)" : "";
                voiceComboBox.Items.Add($"{voice.Id} - {voice.Name}{size} [{voice.EngineType}] {status}");
                if (!string.IsNullOrWhiteSpace(previouslySelectedId) &&
                    voice.Id.Equals(previouslySelectedId, StringComparison.OrdinalIgnoreCase))
                {
                    preferredIndex = index;
                }
                index++;
            }

            if (voiceComboBox.Items.Count > 0)
            {
                if (preferredIndex >= 0)
                    voiceComboBox.SelectedIndex = preferredIndex;
                else if (!hasFilter)
                    voiceComboBox.SelectedIndex = 0;
            }
            suppressComboEvents = false;

            statusLabel!.Text = $"Status: {voiceComboBox.Items.Count} model(s) available";
        }

        private void VoiceComboBox_TextUpdate(object? sender, EventArgs e)
        {
            if (suppressComboEvents || voiceComboBox == null)
                return;

            string filter = voiceComboBox.Text?.Trim() ?? string.Empty;
            string language = languageComboBox?.SelectedItem?.ToString() ?? AllLanguagesOption;
            UpdateVoiceList(language, filter);

            if (!string.IsNullOrWhiteSpace(filter))
            {
                voiceComboBox.Text = filter;
                voiceComboBox.SelectionStart = filter.Length;
                voiceComboBox.SelectionLength = 0;
                voiceComboBox.DroppedDown = true;
            }
        }

        private void VoiceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var voice = GetSelectedVoice();
            if (voice != null)
            {
                bool hasLocalDir = HasModelDirectory(voice.Id);
                bool isReady = IsModelDownloaded(voice.Id);
                downloadButton!.BackColor = Color.FromArgb(255, 140, 0);
                downloadButton!.Enabled = !isReady;
                downloadButton.Text = isReady ? "Downloaded" : (hasLocalDir ? "Repair Download" : "Download Model");
                downloadProgressLabel!.Visible = false;
                if (hasLocalDir && !isReady)
                {
                    string? validation = ValidateSingleLocalModel(Path.Combine(ModelsDir, voice.Id));
                    modelInfoLabel!.Text = $"Local files exist but model is incomplete: {validation}. Click Repair Download.";
                    modelInfoLabel.ForeColor = Color.FromArgb(200, 120, 80);
                }
                else
                {
                    modelInfoLabel!.Text = $"Select a language and model to download. Models are cached in {ModelsDir}\\";
                    modelInfoLabel.ForeColor = Color.FromArgb(120, 120, 120);
                }
            }
            UpdateCompatAliasCheckboxForSelection(voice);
        }

        private void UpdateCompatAliasCheckboxForSelection(VoiceInfo? voice)
        {
            if (enUsCompatAliasCheckBox == null)
                return;

            suppressCompatAliasEvents = true;
            try
            {
                if (voice == null)
                {
                    enUsCompatAliasCheckBox.Checked = false;
                    enUsCompatAliasCheckBox.Enabled = false;
                    return;
                }

                bool downloaded = IsModelDownloaded(voice.Id);
                enUsCompatAliasCheckBox.Enabled = downloaded;
                enUsCompatAliasCheckBox.Checked = downloaded && IsEnUsCompatibilityAliasEnabled(voice.Id);
            }
            finally
            {
                suppressCompatAliasEvents = false;
            }
        }

        private void EnUsCompatAliasCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (suppressCompatAliasEvents)
                return;

            var voice = GetSelectedVoice();
            if (voice == null || enUsCompatAliasCheckBox == null)
                return;

            if (!IsModelDownloaded(voice.Id))
            {
                suppressCompatAliasEvents = true;
                enUsCompatAliasCheckBox.Checked = false;
                suppressCompatAliasEvents = false;
                return;
            }

            bool enabled = enUsCompatAliasCheckBox.Checked;
            SetEnUsCompatibilityAliasEnabled(voice.Id, enabled);
            AppendOutput(
                enabled
                    ? $"en-US compatibility alias enabled for {voice.Id}."
                    : $"en-US compatibility alias disabled for {voice.Id}.",
                Color.FromArgb(180, 220, 255));
            TrySyncPersistentTokens("compat-alias change");
        }

        private VoiceInfo? GetSelectedVoice()
        {
            if (voiceComboBox?.SelectedItem == null) return null;

            string selected = voiceComboBox.SelectedItem.ToString() ?? "";
            // Extract model ID from format: "model-id - name (size) [engine] [status]"
            int endIndex = selected.IndexOf(" - ");
            if (endIndex > 0)
            {
                return AllVoices.FirstOrDefault(v => v.Id == selected.Substring(0, endIndex));
            }
            return null;
        }

        private void DownloadButton_Click(object? sender, EventArgs e)
        {
            if (downloadWorker!.IsBusy)
            {
                RequestDownloadCancellation();
                return;
            }

            var voice = GetSelectedVoice();
            if (voice == null) return;

            AppendOutput($"\r\n=== Downloading Model: {voice.Id} ===", Color.FromArgb(255, 140, 0));
            if (voice.ModelSize > 0)
                AppendOutput($"Size: {voice.ModelSize:F2} MB", Color.FromArgb(200, 200, 200));
            statusLabel!.Text = $"Status: Downloading {voice.Id}...";

            progressBar!.Visible = true;
            progressBar.Value = 0;
            progressBar.Style = ProgressBarStyle.Continuous;
            downloadProgressLabel!.Visible = true;
            downloadProgressLabel.Text = "Preparing download...";
            downloadButton!.Enabled = true;
            downloadButton.Text = "Cancel Download";
            downloadButton.BackColor = Color.FromArgb(190, 50, 45);
            voiceComboBox!.Enabled = false;
            languageComboBox!.Enabled = false;

            downloadWorker.RunWorkerAsync(voice);
        }

        private void RequestDownloadCancellation()
        {
            if (downloadWorker == null || !downloadWorker.IsBusy)
                return;

            downloadButton!.Enabled = false;
            downloadProgressLabel!.Visible = true;
            downloadProgressLabel.Text = "Cancelling download...";
            statusLabel!.Text = "Status: Cancelling download...";
            downloadWorker.CancelAsync();
        }

        private void DownloadWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            var voice = e.Argument as VoiceInfo;
            if (voice == null) return;

            try
            {
                string modelDir = Path.Combine(ModelsDir, voice.Id);
                Directory.CreateDirectory(modelDir);
                activeDownloadModelDir = modelDir;
                activeDownloadArchive = null;

                if (string.IsNullOrEmpty(voice.ModelUrl))
                {
                    this.Invoke((Action)(() =>
                        AppendOutput("ERROR: No download URL available", Color.FromArgb(255, 100, 100))));
                    return;
                }

                downloadWorker!.ReportProgress(10);
                ThrowIfCancellationRequested();

                if (voice.ModelUrl.EndsWith(".tar.bz2") || voice.ModelUrl.Contains("tar.bz2"))
                {
                    DownloadTarArchive(voice, modelDir);
                }
                else if (TryParseHuggingFaceFolderUrl(voice.ModelUrl, out _, out _, out _))
                {
                    DownloadHuggingFaceFolder(voice, modelDir);
                }
                else
                {
                    downloadWorker.ReportProgress(100);
                    this.Invoke((Action)(() =>
                        AppendOutput($"ERROR: Unknown URL format: {voice.ModelUrl}", Color.FromArgb(255, 100, 100))));
                    return;
                }

                ThrowIfCancellationRequested();
                downloadWorker.ReportProgress(100);
                this.Invoke((Action)(() =>
                {
                    AppendOutput($"\r✓ Model downloaded to {modelDir}", Color.FromArgb(100, 255, 100));
                    statusLabel!.Text = $"Status: {voice.Id} downloaded";
                    UpdateVoiceList(languageComboBox!.SelectedItem?.ToString() ?? AllLanguagesOption, null, voice.Id);
                    TrySyncPersistentTokens("download");
                }));
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
                CleanupPartialDownload();
                this.Invoke((Action)(() =>
                {
                    AppendOutput("Download cancelled. Partial files were removed.", Color.FromArgb(255, 200, 100));
                    statusLabel!.Text = "Status: Download cancelled";
                }));
            }
            catch (Exception ex)
            {
                CleanupPartialDownload();
                this.Invoke((Action)(() =>
                {
                    AppendOutput($"\rERROR: {ex.Message}", Color.FromArgb(255, 100, 100));
                    statusLabel!.Text = "Status: Download failed";
                }));
            }
            finally
            {
                activeDownloadArchive = null;
                activeDownloadModelDir = null;
            }
        }

        private void DownloadWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            if (progressBar == null || downloadProgressLabel == null || statusLabel == null)
                return;

            bool extracting = e.UserState is string stateMessage &&
                              stateMessage.StartsWith("Extracting ", StringComparison.OrdinalIgnoreCase);
            if (extracting)
            {
                if (progressBar.Style != ProgressBarStyle.Marquee)
                    progressBar.Style = ProgressBarStyle.Marquee;
            }
            else
            {
                if (progressBar.Style != ProgressBarStyle.Continuous)
                    progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, e.ProgressPercentage));
            }

            if (e.UserState is string message && !string.IsNullOrWhiteSpace(message))
            {
                downloadProgressLabel.Visible = true;
                downloadProgressLabel.Text = message;
                statusLabel.Text = $"Status: {message}";
            }
        }

        private void DownloadWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            progressBar!.Visible = false;
            progressBar.Style = ProgressBarStyle.Continuous;
            downloadProgressLabel!.Visible = false;
            voiceComboBox!.Enabled = true;
            languageComboBox!.Enabled = true;
            downloadButton!.Text = "Download Model";
            downloadButton.BackColor = Color.FromArgb(255, 140, 0);
            VoiceComboBox_SelectedIndexChanged(null, EventArgs.Empty);
        }

        private void ThrowIfCancellationRequested()
        {
            if (downloadWorker?.CancellationPending == true)
                throw new OperationCanceledException("Download cancelled by user.");
        }

        private void CleanupPartialDownload()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(activeDownloadArchive) && File.Exists(activeDownloadArchive))
                {
                    File.Delete(activeDownloadArchive);
                }
            }
            catch
            {
                // Ignore cleanup errors; best-effort only.
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(activeDownloadModelDir) && Directory.Exists(activeDownloadModelDir))
                {
                    Directory.Delete(activeDownloadModelDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors; best-effort only.
            }
        }

        private void DownloadTarArchive(VoiceInfo voice, string modelDir)
        {
            string tarFile = Path.Combine(modelDir, "model.tar.bz2");
            activeDownloadArchive = tarFile;

            this.Invoke((Action)(() =>
                AppendOutput($"Downloading from {voice.ModelUrl}...", Color.FromArgb(150, 200, 255))));
            downloadWorker!.ReportProgress(5, "Connecting to download source...");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(30);
                var response = SendWithRetry(client, voice.ModelUrl, "model archive");
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? 0;
                long downloadedBytes = 0;
                int lastReportedPercent = -1;

                using (var fs = File.Create(tarFile))
                {
                    var stream = response.Content.ReadAsStreamAsync().Result;
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ThrowIfCancellationRequested();
                        fs.Write(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;
                        if (totalBytes > 0)
                        {
                            int downloadPercent = (int)((downloadedBytes * 100) / totalBytes);
                            int progress = 10 + (int)((downloadedBytes * 80) / totalBytes);
                            if (downloadPercent != lastReportedPercent)
                            {
                                lastReportedPercent = downloadPercent;
                                double downloadedMb = downloadedBytes / (1024.0 * 1024.0);
                                double totalMb = totalBytes / (1024.0 * 1024.0);
                                downloadWorker!.ReportProgress(progress,
                                    $"Downloading {voice.Id}: {downloadedMb:F1}/{totalMb:F1} MB ({downloadPercent}%)");
                            }
                        }
                    }
                }
            }

            ThrowIfCancellationRequested();
            downloadWorker!.ReportProgress(90, $"Extracting {voice.Id}... (starting)");
            this.Invoke((Action)(() => AppendOutput("Extracting...", Color.FromArgb(150, 200, 255))));

            // Extract using tar
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xf \"{tarFile}\" -C \"{modelDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (Process process = Process.Start(psi)!)
            {
                var extractStart = Stopwatch.StartNew();
                while (!process.WaitForExit(1000))
                {
                    ThrowIfCancellationRequested();
                    int elapsed = (int)extractStart.Elapsed.TotalSeconds;
                    int progress = Math.Min(97, 90 + Math.Max(1, elapsed / 2));
                    downloadWorker.ReportProgress(progress, $"Extracting {voice.Id}... {elapsed}s");
                }

                ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    string err = process.StandardError.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(err))
                        err = $"tar exited with code {process.ExitCode}";
                    throw new InvalidOperationException($"Extraction failed: {err.Trim()}");
                }
            }

            // Clean up tar file
            File.Delete(tarFile);
            activeDownloadArchive = null;
            downloadWorker!.ReportProgress(98, $"Finalizing {voice.Id}...");
        }

        private void DownloadHuggingFaceFolder(VoiceInfo voice, string modelDir)
        {
            if (!TryParseHuggingFaceFolderUrl(voice.ModelUrl, out string repo, out string revision, out string folderPath))
            {
                throw new InvalidOperationException($"Invalid Hugging Face model URL: {voice.ModelUrl}");
            }

            this.Invoke((Action)(() =>
                AppendOutput($"Downloading Hugging Face model files from {repo}/{folderPath}...", Color.FromArgb(150, 200, 255))));
            downloadWorker!.ReportProgress(5, "Querying Hugging Face model files...");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(30);
                var files = GetHuggingFaceTreeFiles(client, repo, revision, folderPath);
                if (files.Count == 0)
                {
                    throw new InvalidOperationException($"No files found for Hugging Face path '{folderPath}'.");
                }

                for (int i = 0; i < files.Count; i++)
                {
                    ThrowIfCancellationRequested();
                    var file = files[i];
                    string remotePath = file.path ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(remotePath))
                        continue;

                    string relativePath = remotePath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase)
                        ? remotePath.Substring(folderPath.Length + 1)
                        : Path.GetFileName(remotePath);
                    if (string.IsNullOrWhiteSpace(relativePath))
                        continue;

                    string localPath = Path.Combine(modelDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    string? localDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrWhiteSpace(localDir))
                        Directory.CreateDirectory(localDir);

                    int progress = 10 + (int)(((i + 1) * 80.0) / files.Count);
                    downloadWorker.ReportProgress(progress, $"Downloading {voice.Id}: {relativePath} ({i + 1}/{files.Count})");

                    string fileUrl = BuildHuggingFaceResolveUrl(repo, revision, remotePath);
                    using (var response = SendWithRetry(client, fileUrl, $"file {relativePath}"))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var stream = response.Content.ReadAsStreamAsync().Result)
                        using (var fs = File.Create(localPath))
                        {
                            CopyStreamWithCancellation(stream, fs);
                        }
                    }
                }
            }

            string? validationError = ValidateSingleLocalModel(modelDir);
            if (!string.IsNullOrEmpty(validationError))
            {
                throw new InvalidOperationException($"Downloaded files are incomplete: {validationError}");
            }

            downloadWorker!.ReportProgress(98, $"Finalizing {voice.Id}...");
        }

        private void CopyStreamWithCancellation(Stream source, Stream destination)
        {
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
            }
        }

        private async void TestVoiceButton_Click(object? sender, EventArgs e)
        {
            var voice = GetSelectedVoice();
            if (voice == null)
            {
                MessageBox.Show("Please download the model first.", "Model Not Downloaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!IsModelDownloaded(voice.Id))
            {
                string modelDir = Path.Combine(ModelsDir, voice.Id);
                string? validationError = Directory.Exists(modelDir) ? ValidateSingleLocalModel(modelDir) : "Model files are missing";
                MessageBox.Show($"Model is not ready yet: {validationError}", "Model Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string testText = testTextInput!.Text.Trim();
            if (string.IsNullOrEmpty(testText))
                testText = "The quick brown fox jumps over the lazy dog.";

            testVoiceButton!.Enabled = false;
            AppendOutput($"\rTesting voice {voice.Id} via SAPI5...", Color.FromArgb(150, 200, 255));

            try
            {
                Task<TestVoiceResult> speakTask = RunSapiTestOnStaThread(voice.Id, testText);
                Task completed = await Task.WhenAny(speakTask, Task.Delay(TimeSpan.FromSeconds(30)));
                if (completed != speakTask)
                {
                    AppendOutput("\rTest timed out after 30s. The SAPI engine likely stalled during model init/generation.",
                        Color.FromArgb(255, 180, 100));
                    return;
                }

                TestVoiceResult result = await speakTask;
                AppendOutput(result.Message, result.Success ? Color.FromArgb(100, 255, 100) : Color.FromArgb(255, 200, 100));
            }
            catch (Exception ex)
            {
                AppendOutput($"\rERROR testing voice: {ex.Message}", Color.FromArgb(255, 100, 100));
            }
            finally
            {
                testVoiceButton.Enabled = true;
            }
        }

        private static Task<TestVoiceResult> RunSapiTestOnStaThread(string voiceId, string testText)
        {
            var tcs = new TaskCompletionSource<TestVoiceResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            Thread thread = new Thread(() =>
            {
                object? voiceObjRaw = null;
                object? voices = null;
                object? selectedVoiceToken = null;
                try
                {
                    if (!TryValidateSapiRegistration(out string regReason))
                    {
                        tcs.TrySetResult(new TestVoiceResult(false, "\r" + regReason));
                        return;
                    }

                    Type? spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                    if (spVoiceType == null)
                    {
                        tcs.TrySetResult(new TestVoiceResult(false, "\rSAPI5 not available on this system."));
                        return;
                    }

                    voiceObjRaw = Activator.CreateInstance(spVoiceType);
                    if (voiceObjRaw == null)
                    {
                        tcs.TrySetResult(new TestVoiceResult(false, "\rFailed to create SAPI.SpVoice instance."));
                        return;
                    }

                    // Preferred path: bind concrete persistent token IDs first.
                    string[] directTokenIds = new[]
                    {
                        $@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-{voiceId}",
                        $@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-{voiceId}"
                    };
                    string directTokenId = directTokenIds[0];
                    string directBindErrors = string.Empty;
                    Type? spTokenType = Type.GetTypeFromProgID("SAPI.SpObjectToken");
                    if (spTokenType != null)
                    {
                        foreach (string candidateTokenId in directTokenIds)
                        {
                            object? directToken = null;
                            try
                            {
                                directTokenId = candidateTokenId;
                                directToken = Activator.CreateInstance(spTokenType);
                                if (directToken != null)
                                {
                                    InvokeComMethod(directToken, "SetId", directTokenId, false);
                                    SetComProperty(voiceObjRaw, "Voice", directToken);
                                    InvokeComMethod(voiceObjRaw, "Speak", testText);
                                    tcs.TrySetResult(new TestVoiceResult(true, $"\r✓ Played test using {voiceId} ({directTokenId})"));
                                    return;
                                }
                            }
                            catch (Exception directEx)
                            {
                                Exception directRoot = directEx;
                                while (directRoot is TargetInvocationException dtie && dtie.InnerException != null)
                                {
                                    directRoot = dtie.InnerException;
                                }
                                string directDetails = directRoot is COMException directCom
                                    ? $"{directCom.Message} (HRESULT 0x{directCom.HResult:X8})"
                                    : directRoot.Message;
                                directBindErrors += $"{candidateTokenId} => {directDetails}; ";
                            }
                            finally
                            {
                                ReleaseComObject(directToken);
                            }
                        }
                    }

                    // Enumerate only VoiceGarden Sherpa tokens to avoid unrelated broken tokens.
                    voices = InvokeComMethod(voiceObjRaw, "GetVoices", "Vendor=K2FSA", "");
                    if (voices == null)
                    {
                        tcs.TrySetResult(new TestVoiceResult(false, "\rNo SAPI voices collection returned for Vendor=K2FSA."));
                        return;
                    }

                    int count = Convert.ToInt32(GetComProperty(voices, "Count") ?? 0, CultureInfo.InvariantCulture);
                    if (count == 0)
                    {
                        tcs.TrySetResult(new TestVoiceResult(false,
                            "\rNo Sherpa voices found in SAPI (Vendor=K2FSA). Re-register 64-bit and rescan models."));
                        return;
                    }

                    bool found = false;
                    string selectedVoiceTokenId = string.Empty;
                    object? bestToken = null;
                    int bestScore = int.MinValue;
                    for (int i = 0; i < count; i++)
                    {
                        object? v = null;
                        try
                        {
                            v = InvokeComMethod(voices, "Item", i);
                            string? id = GetComProperty(v, "Id")?.ToString();
                            if (!string.IsNullOrWhiteSpace(id) &&
                                id.IndexOf(voiceId, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                int score = 0;
                                string hklmExact = $@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-{voiceId}";
                                string hkcuExact = $@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-{voiceId}";
                                if (id.Equals(hklmExact, StringComparison.OrdinalIgnoreCase)) score = 300;
                                else if (id.Equals(hkcuExact, StringComparison.OrdinalIgnoreCase)) score = 200;
                                else if (id.StartsWith(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-", StringComparison.OrdinalIgnoreCase)) score = 150;
                                else if (id.StartsWith(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-", StringComparison.OrdinalIgnoreCase)) score = 100;
                                else score = 10;

                                if (score > bestScore)
                                {
                                    ReleaseComObject(bestToken);
                                    bestToken = v;
                                    v = null;
                                    bestScore = score;
                                    selectedVoiceTokenId = id;
                                }
                            }
                        }
                        catch
                        {
                            // Skip malformed token and continue.
                        }
                        finally
                        {
                            ReleaseComObject(v);
                        }
                    }

                    if (bestToken != null)
                    {
                        try
                        {
                            SetComProperty(voiceObjRaw, "Voice", bestToken);
                            selectedVoiceToken = bestToken;
                            bestToken = null; // transfer ownership
                            found = true;
                        }
                        catch (Exception setVoiceEx)
                        {
                            Exception setRoot = setVoiceEx;
                            while (setRoot is TargetInvocationException stie && stie.InnerException != null)
                            {
                                setRoot = stie.InnerException;
                            }
                            string setDetails = setRoot is COMException setCom
                                ? $"{setCom.Message} (HRESULT 0x{setCom.HResult:X8})"
                                : setRoot.Message;
                            tcs.TrySetResult(new TestVoiceResult(false,
                                $"\rERROR testing voice at Set Voice token: {setDetails} [{setRoot.GetType().Name}] token={selectedVoiceTokenId} {GetSapiRegistrationSnapshot()}"));
                            return;
                        }
                        finally
                        {
                            ReleaseComObject(bestToken);
                        }
                    }

                    if (!found)
                    {
                        var availableIds = new List<string>();
                        for (int i = 0; i < count; i++)
                        {
                            object? v = null;
                            try
                            {
                                v = InvokeComMethod(voices, "Item", i);
                                string? id = GetComProperty(v, "Id")?.ToString();
                                if (!string.IsNullOrWhiteSpace(id))
                                    availableIds.Add(id);
                            }
                            catch
                            {
                                // Ignore bad token for diagnostics list.
                            }
                            finally
                            {
                                ReleaseComObject(v);
                            }
                        }
                        string shortList = string.Join(", ", availableIds.Take(5));
                        if (availableIds.Count > 5)
                            shortList += ", ...";
                        tcs.TrySetResult(new TestVoiceResult(false,
                            $"\rVoice {voiceId} not found in SAPI5 Vendor=K2FSA set. Available voices: {shortList}"));
                        return;
                    }

                    try
                    {
                        InvokeComMethod(voiceObjRaw, "Speak", testText);
                    }
                    catch (Exception speakEx)
                    {
                        Exception speakRoot = speakEx;
                        while (speakRoot is TargetInvocationException tie && tie.InnerException != null)
                        {
                            speakRoot = tie.InnerException;
                        }

                        string speakDetails = speakRoot is COMException speakCom
                            ? $"{speakCom.Message} (HRESULT 0x{speakCom.HResult:X8})"
                            : speakRoot.Message;

                        tcs.TrySetResult(new TestVoiceResult(false,
                            $"\rERROR testing voice at Speak call: {speakDetails} [{speakRoot.GetType().Name}] tokenHint={directTokenId} directBind={directBindErrors} {GetSapiRegistrationSnapshot()}"));
                        return;
                    }
                    tcs.TrySetResult(new TestVoiceResult(true, $"\r✓ Played test using {voiceId} ({selectedVoiceTokenId})"));
                }
                catch (Exception ex)
                {
                    Exception root = ex;
                    while (root is TargetInvocationException tie && tie.InnerException != null)
                    {
                        root = tie.InnerException;
                    }

                    string details = root.Message;
                    if (root is COMException comEx)
                    {
                        details = $"{comEx.Message} (HRESULT 0x{comEx.HResult:X8})";
                    }

                    string regSnapshot = GetSapiRegistrationSnapshot();
                    tcs.TrySetResult(new TestVoiceResult(
                        false,
                        $"\rERROR testing voice: {details} [{root.GetType().Name}] {regSnapshot}"));
                }
                finally
                {
                    ReleaseComObject(selectedVoiceToken);
                    ReleaseComObject(voices);
                    ReleaseComObject(voiceObjRaw);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private static void ReleaseComObject(object? obj)
        {
            if (obj == null)
                return;

            try
            {
                if (Marshal.IsComObject(obj))
                    Marshal.FinalReleaseComObject(obj);
            }
            catch
            {
                // Ignore release errors in test diagnostics path.
            }
        }

        private static bool TryValidateSapiRegistration(out string reason)
        {
            string ttsInproc = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                $@"SOFTWARE\Classes\CLSID\{TtsEngineClsid}\InprocServer32", null);
            string enumInproc = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                $@"SOFTWARE\Classes\CLSID\{VoiceEnumClsid}\InprocServer32", null);
            string tokenEnumClsid = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                TokenEnumKeyPath, "CLSID");
            string userTokenEnumClsid = ReadRegistryString(RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Speech\Voices\TokenEnums\VoiceGardenEnumerator", "CLSID");

            if (string.IsNullOrWhiteSpace(ttsInproc) || string.IsNullOrWhiteSpace(enumInproc))
            {
                reason = "SAPI COM registration is missing in HKLM. Run Installer as Administrator and click Register 64-bit.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tokenEnumClsid))
            {
                if (!string.IsNullOrWhiteSpace(userTokenEnumClsid))
                {
                    reason = "TokenEnums exists only under HKCU. Register the DLL as Administrator so HKLM TokenEnums is created.";
                }
                else
                {
                    reason = "HKLM TokenEnums registration is missing. Run Installer as Administrator and click Register 64-bit.";
                }
                return false;
            }

            if (!string.Equals(tokenEnumClsid, VoiceEnumClsid, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"HKLM TokenEnums CLSID mismatch: {tokenEnumClsid}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static string GetSapiRegistrationSnapshot()
        {
            string ttsInproc = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                $@"SOFTWARE\Classes\CLSID\{TtsEngineClsid}\InprocServer32", null);
            string enumInproc = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                $@"SOFTWARE\Classes\CLSID\{VoiceEnumClsid}\InprocServer32", null);
            string tokenEnumClsid = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                TokenEnumKeyPath, "CLSID");
            string userTokenEnumClsid = ReadRegistryString(RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Speech\Voices\TokenEnums\VoiceGardenEnumerator", "CLSID");

            var sb = new StringBuilder();
            sb.Append("(reg: ");
            sb.Append("HKLM.TTS=");
            sb.Append(string.IsNullOrWhiteSpace(ttsInproc) ? "<missing>" : ttsInproc);
            sb.Append("; HKLM.Enum=");
            sb.Append(string.IsNullOrWhiteSpace(enumInproc) ? "<missing>" : enumInproc);
            sb.Append("; HKLM.TokenEnum=");
            sb.Append(string.IsNullOrWhiteSpace(tokenEnumClsid) ? "<missing>" : tokenEnumClsid);
            sb.Append("; HKCU.TokenEnum=");
            sb.Append(string.IsNullOrWhiteSpace(userTokenEnumClsid) ? "<missing>" : userTokenEnumClsid);
            sb.Append(')');
            return sb.ToString();
        }

        private static string ReadRegistryString(RegistryHive hive, RegistryView view, string subKey, string? valueName)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
                return key?.GetValue(valueName ?? string.Empty) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static object? InvokeComMethod(object? target, string name, params object[] args)
        {
            if (target == null)
                return null;
            return target.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                args,
                CultureInfo.InvariantCulture);
        }

        private static object? GetComProperty(object? target, string name)
        {
            if (target == null)
                return null;
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                null,
                CultureInfo.InvariantCulture);
        }

        private static void SetComProperty(object? target, string name, object? value)
        {
            if (target == null)
                return;
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                new[] { value },
                CultureInfo.InvariantCulture);
        }

        private sealed class TestVoiceResult
        {
            public bool Success { get; }
            public string Message { get; }

            public TestVoiceResult(bool success, string message)
            {
                Success = success;
                Message = message;
            }
        }

        private void OpenModelsFolderButton_Click(object? sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(ModelsDir);
                Process.Start("explorer.exe", ModelsDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RescanModelsButton_Click(object? sender, EventArgs e)
        {
            PerformLocalModelRescan();
        }

        private void InstallForAdminAppsButton_Click(object? sender, EventArgs e)
        {
            var voice = GetSelectedVoice();
            if (voice == null)
            {
                MessageBox.Show("Select a model first.", "No Model Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!IsModelDownloaded(voice.Id))
            {
                MessageBox.Show("Download the model first.", "Model Not Downloaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppendOutput($"\r\n=== Installing for Admin Apps: {voice.Id} ===", Color.FromArgb(120, 200, 255));
            string selectedModelDir = Path.Combine(ModelsDir, voice.Id);
            bool installCompatAlias = enUsCompatAliasCheckBox?.Checked == true;

            try
            {
                int rc = PromoteModelTokenToHklm(voice.Id, selectedModelDir, installCompatAlias);
                if (rc == 0)
                {
                    AppendOutput($"✓ Installed {voice.Id} to HKLM tokens. Restart target apps to refresh voice list.",
                        Color.FromArgb(100, 255, 100));
                    return;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to elevation path.
            }
            catch (Exception ex)
            {
                AppendOutput($"Promotion failed: {ex.Message}", Color.FromArgb(255, 140, 120));
                return;
            }

            try
            {
                string exePath = Application.ExecutablePath;
                string args = $"promote-hklm \"{voice.Id}\" --model-dir \"{selectedModelDir}\"";
                if (installCompatAlias)
                    args += " --compat-en-us";
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using Process? elevated = Process.Start(psi);
                if (elevated == null)
                {
                    AppendOutput("Promotion cancelled.", Color.FromArgb(255, 200, 100));
                    return;
                }

                elevated.WaitForExit();
                if (elevated.ExitCode == 0)
                {
                    AppendOutput($"✓ Installed {voice.Id} to HKLM tokens (elevated). Restart target apps to refresh voice list.",
                        Color.FromArgb(100, 255, 100));
                }
                else
                {
                    AppendOutput($"Promotion failed (exit code {elevated.ExitCode}).", Color.FromArgb(255, 140, 120));
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                AppendOutput("Promotion cancelled by user.", Color.FromArgb(255, 200, 100));
            }
            catch (Exception ex)
            {
                AppendOutput($"Could not start elevated promotion: {ex.Message}", Color.FromArgb(255, 140, 120));
            }
        }

        private void PerformLocalModelRescan()
        {
            AppendOutput($"\r\n=== Rescanning local models in {ModelsDir} ===", Color.FromArgb(120, 200, 255));
            var result = ScanLocalModels();
            s_lastScanIssues = result.Issues;
            PersistLastScanIssues(result.Issues);
            var syncResult = SyncPersistentSherpaTokens(result);

            if (result.TotalDirectories == 0)
            {
                AppendOutput("No local model directories found.", Color.FromArgb(255, 200, 100));
            }
            else
            {
                AppendOutput($"Valid models: {result.ValidModels}/{result.TotalDirectories}", Color.FromArgb(100, 255, 100));

                if (result.Issues.Count == 0)
                {
                    AppendOutput("No scan errors detected.", Color.FromArgb(100, 255, 100));
                }
                else
                {
                    AppendOutput($"Scan errors: {result.Issues.Count}", Color.FromArgb(255, 180, 100));
                    foreach (var issue in result.Issues.OrderBy(i => i.ModelId))
                    {
                        AppendOutput($"  {issue.ModelId}: {issue.Error}", Color.FromArgb(255, 120, 120));
                    }
                }
            }

            AppendOutput($"Persistent SAPI Sherpa tokens ({syncResult.RegistryScope}): +{syncResult.Added} ~{syncResult.Updated} -{syncResult.Removed}",
                syncResult.Error == null ? Color.FromArgb(120, 220, 140) : Color.FromArgb(255, 180, 100));
            foreach (string warning in syncResult.Warnings.Take(5))
            {
                AppendOutput($"  Token sync note: {warning}", Color.FromArgb(255, 180, 120));
            }
            if (syncResult.Warnings.Count > 5)
            {
                AppendOutput($"  ... and {syncResult.Warnings.Count - 5} more token warnings", Color.FromArgb(255, 180, 120));
            }
            if (!string.IsNullOrWhiteSpace(syncResult.Error))
            {
                AppendOutput($"  Token sync warning: {syncResult.Error}", Color.FromArgb(255, 140, 120));
            }
        }

        private void AppendOutput(string text, Color color)
        {
            outputTextBox!.SelectionStart = outputTextBox.TextLength;
            outputTextBox.SelectionColor = color;
            outputTextBox.AppendText(text + "\r\n");
            outputTextBox.SelectionStart = outputTextBox.TextLength;
            outputTextBox.ScrollToCaret();
        }

        private void TrySyncPersistentTokens(string reason)
        {
            try
            {
                var scan = ScanLocalModels();
                var sync = SyncPersistentSherpaTokens(scan);
                AppendOutput(
                    $"Token sync ({reason}, {sync.RegistryScope}): +{sync.Added} ~{sync.Updated} -{sync.Removed}",
                    string.IsNullOrWhiteSpace(sync.Error) ? Color.FromArgb(120, 220, 140) : Color.FromArgb(255, 180, 100));
                if (!string.IsNullOrWhiteSpace(sync.Error))
                {
                    AppendOutput($"  Token sync warning: {sync.Error}", Color.FromArgb(255, 140, 120));
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Token sync ({reason}) failed: {ex.Message}", Color.FromArgb(255, 140, 120));
            }
        }

        // Static methods for CLI access
        public static int ListVoices(string? languageFilter = null)
        {
            Console.WriteLine($"SherpaOnnx Model Manager - Available Models");
            Console.WriteLine($"=========================================");

            try
            {
                string? catalogPath = FindCatalogPath();
                if (string.IsNullOrEmpty(catalogPath))
                {
                    Console.WriteLine("ERROR: models.json not found!");
                    return 1;
                }

                string json = File.ReadAllText(catalogPath);
                var catalog = JsonSerializer.Deserialize<SherpaModelsCatalog>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (catalog == null)
                {
                    Console.WriteLine("ERROR: Failed to parse catalog!");
                    return 1;
                }

                Console.WriteLine($"Total models: {catalog.Count}\n");

                // Group by language
                var langGroups = new Dictionary<string, List<SherpaModelInfo>>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in catalog)
                {
                    var model = kvp.Value;
                    var languageNames = GetLanguageDisplayNames(model).ToList();
                    if (languageNames.Count == 0)
                        continue;

                    bool includeByFilter = string.IsNullOrEmpty(languageFilter) ||
                        languageNames.Any(lang =>
                            lang.Equals(languageFilter, StringComparison.OrdinalIgnoreCase) ||
                            lang.Contains(languageFilter, StringComparison.OrdinalIgnoreCase));

                    if (!includeByFilter)
                        continue;

                    foreach (var langStr in languageNames)
                    {
                        if (!langGroups.ContainsKey(langStr))
                            langGroups[langStr] = new List<SherpaModelInfo>();
                        langGroups[langStr].Add(model);
                    }
                }

                // Display grouped by language
                foreach (var langGroup in langGroups.OrderBy(x => x.Key))
                {
                    Console.WriteLine($"\n[{langGroup.Key}]");
                    foreach (var model in langGroup.Value.OrderBy(m => m.name))
                    {
                        string url = model.url ?? model.url_ ?? "";
                        double? sizeValue = model.filesize_mb;
                        string size = (sizeValue != null && sizeValue.Value > 0)
                            ? $"{sizeValue.Value:F1} MB"
                            : "Unknown";

                        // Check if downloaded
                        bool downloaded = IsModelDownloaded(model.id ?? "");
                        string status = downloaded ? "✓ Downloaded" : "Not downloaded";

                        string displayName = string.IsNullOrWhiteSpace(model.name) ? (model.id ?? "Unknown") : model.name;
                        Console.WriteLine($"  {model.id,-40} {displayName,-30} [{size}] [{status}]");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return 1;
            }
        }

        public static int DownloadModel(string modelId)
        {
            Console.WriteLine($"SherpaOnnx Model Manager - Download Model");
            Console.WriteLine($"=======================================");
            Console.WriteLine($"Downloading: {modelId}");

            try
            {
                // Find model in catalog
                string? catalogPath = FindCatalogPath();
                if (string.IsNullOrEmpty(catalogPath))
                {
                    Console.WriteLine("ERROR: models.json not found!");
                    return 1;
                }

                string json = File.ReadAllText(catalogPath);
                var catalog = JsonSerializer.Deserialize<SherpaModelsCatalog>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (catalog == null)
                {
                    Console.WriteLine("ERROR: Failed to parse catalog!");
                    return 1;
                }

                SherpaModelInfo? model = null;
                foreach (var kvp in catalog)
                {
                    if ((kvp.Value.id ?? "") == modelId)
                    {
                        model = kvp.Value;
                        break;
                    }
                }

                if (model == null)
                {
                    Console.WriteLine($"ERROR: Model '{modelId}' not found in catalog!");
                    Console.WriteLine("\nUse 'list' command to see available models.");
                    return 1;
                }

                string modelUrl = model.url ?? model.url_ ?? "";
                if (string.IsNullOrEmpty(modelUrl))
                {
                    Console.WriteLine($"ERROR: No download URL for model '{modelId}'!");
                    return 1;
                }

                string modelDir = Path.Combine(ModelsDir, modelId);
                Directory.CreateDirectory(modelDir);

                Console.WriteLine($"URL: {modelUrl}");
                Console.WriteLine($"Destination: {modelDir}");

                // Download
                Console.WriteLine("\nDownloading...");
                if (modelUrl.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase) ||
                    modelUrl.Contains("tar.bz2", StringComparison.OrdinalIgnoreCase))
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromMinutes(30);
                        var response = SendWithRetry(client, modelUrl, "model archive");
                        response.EnsureSuccessStatusCode();

                        long totalBytes = response.Content.Headers.ContentLength ?? 0;
                        if (totalBytes > 0)
                            Console.WriteLine($"Size: {totalBytes / (1024.0 * 1024):F1} MB");

                        string tarFile = Path.Combine(modelDir, "model.tar.bz2");
                        using (var fs = File.Create(tarFile))
                        {
                            var stream = response.Content.ReadAsStreamAsync().Result;
                            stream.CopyToAsync(fs).Wait();
                        }

                        Console.WriteLine("Extracting...");

                        // Extract using tar
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "tar",
                            Arguments = $"-xf \"{tarFile}\" -C \"{modelDir}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using (Process process = Process.Start(psi)!)
                        {
                            process.WaitForExit();
                        }

                        File.Delete(tarFile);
                    }
                }
                else if (TryParseHuggingFaceFolderUrl(modelUrl, out string repo, out string revision, out string folderPath))
                {
                    Console.WriteLine($"Detected Hugging Face model folder: {repo}/{folderPath}");
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromMinutes(30);
                        var files = GetHuggingFaceTreeFiles(client, repo, revision, folderPath);
                        if (files.Count == 0)
                        {
                            Console.WriteLine("ERROR: No files found in Hugging Face folder.");
                            return 1;
                        }

                        Console.WriteLine($"Files to download: {files.Count}");
                        for (int i = 0; i < files.Count; i++)
                        {
                            string remotePath = files[i].path ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(remotePath))
                                continue;

                            string relativePath = remotePath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase)
                                ? remotePath.Substring(folderPath.Length + 1)
                                : Path.GetFileName(remotePath);
                            if (string.IsNullOrWhiteSpace(relativePath))
                                continue;

                            Console.WriteLine($"  [{i + 1}/{files.Count}] {relativePath}");

                            string localPath = Path.Combine(modelDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                            string? localDir = Path.GetDirectoryName(localPath);
                            if (!string.IsNullOrWhiteSpace(localDir))
                                Directory.CreateDirectory(localDir);

                            string fileUrl = BuildHuggingFaceResolveUrl(repo, revision, remotePath);
                            using (var response = SendWithRetry(client, fileUrl, $"file {relativePath}"))
                            {
                                response.EnsureSuccessStatusCode();
                                using (var stream = response.Content.ReadAsStreamAsync().Result)
                                using (var fs = File.Create(localPath))
                                {
                                    stream.CopyTo(fs);
                                }
                            }
                        }
                    }

                    string? validationError = ValidateSingleLocalModel(modelDir);
                    if (!string.IsNullOrEmpty(validationError))
                    {
                        Console.WriteLine($"ERROR: Downloaded files are incomplete: {validationError}");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"ERROR: Unknown URL format: {modelUrl}");
                    return 1;
                }

                Console.WriteLine("\n✓ Model downloaded successfully!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return 1;
            }
        }

        public static int ListDownloaded()
        {
            Console.WriteLine($"SherpaOnnx Model Manager - Downloaded Models");
            Console.WriteLine($"=========================================");
            Console.WriteLine($"Models directory: {ModelsDir}\n");

            if (!Directory.Exists(ModelsDir))
            {
                Console.WriteLine("No models directory found.");
                return 0;
            }

            var downloadedDirs = Directory.GetDirectories(ModelsDir);

            if (downloadedDirs.Length == 0)
            {
                Console.WriteLine("No models downloaded yet.");
                return 0;
            }

            Console.WriteLine($"Downloaded models: {downloadedDirs.Length}\n");

            foreach (var dir in downloadedDirs.OrderBy(d => d))
            {
                string modelId = Path.GetFileName(dir);
                Console.WriteLine($"  {modelId}");

                // List files in model directory
                try
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    Console.WriteLine($"    Files: {files.Length}");
                }
                catch { }
            }

            return 0;
        }

        public static int RescanModels()
        {
            Console.WriteLine("SherpaOnnx Model Manager - Rescan Local Models");
            Console.WriteLine("=============================================");
            Console.WriteLine($"Models directory: {ModelsDir}\n");

            var result = ScanLocalModels();
            s_lastScanIssues = result.Issues;
            PersistLastScanIssues(result.Issues);

            Console.WriteLine($"Model directories found: {result.TotalDirectories}");
            Console.WriteLine($"Valid models: {result.ValidModels}");
            Console.WriteLine($"Errors: {result.Issues.Count}\n");

            foreach (var issue in result.Issues.OrderBy(i => i.ModelId))
            {
                Console.WriteLine($"  {issue.ModelId}: {issue.Error}");
            }

            var syncResult = SyncPersistentSherpaTokens(result);
            Console.WriteLine();
            Console.WriteLine($"Persistent SAPI Sherpa tokens synced ({syncResult.RegistryScope}): +{syncResult.Added} ~{syncResult.Updated} -{syncResult.Removed}");
            foreach (string warning in syncResult.Warnings.Take(10))
            {
                Console.WriteLine($"  note: {warning}");
            }
            if (!string.IsNullOrWhiteSpace(syncResult.Error))
                Console.WriteLine($"Token sync warning: {syncResult.Error}");

            return result.Issues.Count == 0 ? 0 : 2;
        }

        public static int PromoteModelTokenToHklm(string modelId, string? modelDirOverride = null, bool addEnUsCompatAlias = false)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                Console.WriteLine("ERROR: model id is required.");
                return 1;
            }

            string modelDir = !string.IsNullOrWhiteSpace(modelDirOverride)
                ? modelDirOverride
                : Path.Combine(ModelsDir, modelId);
            if (!Directory.Exists(modelDir))
            {
                Console.WriteLine($"ERROR: model directory not found: {modelDir}");
                return 1;
            }

            var catalog = LoadCatalogById();
            if (!TryBuildPersistentTokenMetadata(modelId, modelDir, catalog, out var meta, out string? why))
            {
                Console.WriteLine($"ERROR: cannot build token metadata for {modelId}: {why}");
                return 2;
            }

            using RegistryKey? hklmRoot = Registry.LocalMachine.CreateSubKey(SapiTokensRoot, writable: true);
            if (hklmRoot == null)
            {
                Console.WriteLine("ERROR: cannot open HKLM SAPI tokens root for writing.");
                return 3;
            }

            string tokenName = "Sherpa-" + modelId;
            WritePersistentToken(hklmRoot, tokenName, meta);
            if (ShouldCreateEnUsCompatibilityAlias(addEnUsCompatAlias, meta))
            {
                string aliasTokenName = tokenName + CompatibilityAliasSuffix;
                var aliasMeta = CloneAsEnUsAlias(meta);
                WritePersistentToken(hklmRoot, aliasTokenName, aliasMeta, compatibilityAlias: true);
            }

            var syncResult = new TokenSyncResult { RegistryScope = "HKLM" };
            EnsurePersistentTokenMode(syncResult);

            Console.WriteLine($"Promoted {modelId} to HKLM token: {tokenName}");
            foreach (string warning in syncResult.Warnings.Take(5))
            {
                Console.WriteLine($"  note: {warning}");
            }
            return 0;
        }

        public static int PromoteAllDownloadedToHklm(bool addEnUsCompatAlias, bool adminAll)
        {
            var modelsRoot = ModelsDir;
            if (!Directory.Exists(modelsRoot))
            {
                Console.WriteLine($"ERROR: Models directory not found: {modelsRoot}");
                return 1;
            }

            int promoted = 0, failed = 0;
            foreach (var dir in Directory.GetDirectories(modelsRoot))
            {
                var modelId = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(modelId)) continue;

                Console.Write($"Promoting {modelId}... ");
                try
                {
                    int rc = PromoteModelTokenToHklm(modelId, dir, addEnUsCompatAlias);
                    if (rc == 0)
                    {
                        Console.WriteLine("OK");
                        promoted++;
                    }
                    else
                    {
                        Console.WriteLine($"FAILED (exit {rc})");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                    failed++;
                }
            }

            // Sync token state
            if (promoted > 0)
            {
                var syncResult = new TokenSyncResult { RegistryScope = "HKLM" };
                EnsurePersistentTokenMode(syncResult);
            }

            Console.WriteLine($"\nDone: {promoted} promoted, {failed} failed.");
            return failed > 0 ? 1 : 0;
        }

        private void ShowLastScanIssuesSummary()
        {
            var persistedIssues = LoadPersistedScanIssues();
            if (persistedIssues.Count == 0)
                return;

            AppendOutput($"Last scan reported {persistedIssues.Count} model issue(s). Click 'Rescan Models' for details.", Color.FromArgb(255, 200, 100));
            foreach (var issue in persistedIssues.Take(3))
            {
                AppendOutput($"  {issue.ModelId}: {issue.Error}", Color.FromArgb(255, 160, 120));
            }
            if (persistedIssues.Count > 3)
            {
                AppendOutput($"  ... and {persistedIssues.Count - 3} more", Color.FromArgb(255, 160, 120));
            }
        }

        private static string? FindCatalogPath()
        {
            string[] paths = new string[]
            {
                Path.Combine(AppContext.BaseDirectory, "models.json"),
                Path.Combine(AppContext.BaseDirectory, "sherpa-config", "models.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sherpa-config", "models.json"),
                "models.json"
            };

            foreach (string path in paths)
            {
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private static bool TryParseHuggingFaceFolderUrl(string url, out string repo, out string revision, out string folderPath)
        {
            repo = string.Empty;
            revision = string.Empty;
            folderPath = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return false;
            if (!uri.Host.Contains("huggingface.co", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 5)
                return false;

            int modeIndex = Array.FindIndex(segments, s =>
                s.Equals("resolve", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("tree", StringComparison.OrdinalIgnoreCase));

            if (modeIndex < 2 || modeIndex + 2 >= segments.Length)
                return false;

            repo = $"{segments[0]}/{segments[1]}";
            revision = Uri.UnescapeDataString(segments[modeIndex + 1]);
            folderPath = string.Join("/", segments.Skip(modeIndex + 2).Select(Uri.UnescapeDataString));

            return !string.IsNullOrWhiteSpace(repo) &&
                   !string.IsNullOrWhiteSpace(revision) &&
                   !string.IsNullOrWhiteSpace(folderPath);
        }

        private static List<HuggingFaceTreeEntry> GetHuggingFaceTreeFiles(HttpClient client, string repo, string revision, string folderPath)
        {
            string encodedFolder = EncodePathSegments(folderPath);
            string apiUrl = $"https://huggingface.co/api/models/{repo}/tree/{Uri.EscapeDataString(revision)}/{encodedFolder}";

            string json = GetStringWithRetry(client, apiUrl, "Hugging Face tree API");
            var entries = JsonSerializer.Deserialize<List<HuggingFaceTreeEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<HuggingFaceTreeEntry>();

            return entries
                .Where(e => string.Equals(e.type, "file", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(e.path))
                .ToList();
        }

        private static string BuildHuggingFaceResolveUrl(string repo, string revision, string remotePath)
        {
            string encodedPath = EncodePathSegments(remotePath);
            return $"https://huggingface.co/{repo}/resolve/{Uri.EscapeDataString(revision)}/{encodedPath}?download=true";
        }

        private static string EncodePathSegments(string path)
        {
            return string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        }

        private static HttpResponseMessage SendWithRetry(HttpClient client, string url, string purpose)
        {
            const int maxAttempts = 4;
            Exception? lastEx = null;
            int? lastStatus = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    HttpResponseMessage response = client.GetAsync(url).Result;
                    int code = (int)response.StatusCode;
                    if ((code == 429 || code == 502 || code == 503 || code == 504) && attempt < maxAttempts)
                    {
                        lastStatus = code;
                        response.Dispose();
                        Thread.Sleep(300 * attempt * attempt);
                        continue;
                    }

                    return response;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    lastEx = ex;
                    Thread.Sleep(300 * attempt * attempt);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    break;
                }
            }

            if (lastStatus.HasValue)
                throw new HttpRequestException($"Failed to fetch {purpose} after {maxAttempts} attempts. Last status: {lastStatus.Value}.");

            throw new HttpRequestException($"Failed to fetch {purpose} after {maxAttempts} attempts.", lastEx);
        }

        private static string GetStringWithRetry(HttpClient client, string url, string purpose)
        {
            using HttpResponseMessage response = SendWithRetry(client, url, purpose);
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().Result;
        }

        private static bool IsModelDownloaded(string modelId)
        {
            string modelDir = Path.Combine(ModelsDir, modelId);
            if (!HasModelDirectory(modelId))
                return false;

            try
            {
                string? validationError = ValidateSingleLocalModel(modelDir);
                return string.IsNullOrEmpty(validationError);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasModelDirectory(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return false;
            string modelDir = Path.Combine(ModelsDir, modelId);
            return Directory.Exists(modelDir);
        }

        private static IEnumerable<string> GetLanguageDisplayNames(SherpaModelInfo model)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (model.language != null)
            {
                foreach (var lang in model.language)
                {
                    string? resolved = ResolveLanguageName(lang);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        names.Add(resolved);
                }
            }

            if (names.Count > 0)
                return names;

            return new[] { "Unknown" };
        }

        private static string? ResolveLanguageName(SherpaLanguage? lang)
        {
            if (lang == null)
                return null;

            string? languageName = (lang.language_name ?? lang.LanguageNameAlt)?.Trim();
            if (!string.IsNullOrWhiteSpace(languageName) &&
                !languageName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                !languageName.StartsWith("Unknown language", StringComparison.OrdinalIgnoreCase))
            {
                return languageName;
            }

            string? code = (lang.lang_code ?? lang.IsoCodeAlt)?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return null;

            try
            {
                string normalized = code.ToLowerInvariant();
                var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                    .FirstOrDefault(c =>
                        c.TwoLetterISOLanguageName.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                        c.ThreeLetterISOLanguageName.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                        c.ThreeLetterWindowsLanguageName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                if (culture != null)
                    return culture.EnglishName;
            }
            catch
            {
                // Fall through and return raw code if lookup fails.
            }

            return code.ToUpperInvariant();
        }

        private static ModelScanResult ScanLocalModels()
        {
            var result = new ModelScanResult();
            Directory.CreateDirectory(ModelsDir);

            foreach (string modelDir in Directory.GetDirectories(ModelsDir))
            {
                result.TotalDirectories++;
                string modelId = Path.GetFileName(modelDir);

                string? error = ValidateSingleLocalModel(modelDir);
                if (string.IsNullOrEmpty(error))
                {
                    result.ValidModels++;
                }
                else
                {
                    result.Issues.Add(new ModelScanIssue { ModelId = modelId, Error = error });
                }
            }

            return result;
        }

        private static void PersistLastScanIssues(List<ModelScanIssue> issues)
        {
            try
            {
                Directory.CreateDirectory(AdapterDataDir);
                string json = JsonSerializer.Serialize(issues, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ScanErrorsPath, json);
            }
            catch
            {
                // Best effort only.
            }
        }

        private static List<ModelScanIssue> LoadPersistedScanIssues()
        {
            try
            {
                if (!File.Exists(ScanErrorsPath))
                    return new List<ModelScanIssue>();

                string json = File.ReadAllText(ScanErrorsPath);
                return JsonSerializer.Deserialize<List<ModelScanIssue>>(json) ?? new List<ModelScanIssue>();
            }
            catch
            {
                return new List<ModelScanIssue>();
            }
        }

        private static string? ValidateSingleLocalModel(string modelDir)
        {
            string scanDir = modelDir;
            bool hasTopLevelSignals = File.Exists(Path.Combine(modelDir, "tokens.txt")) ||
                                      File.Exists(Path.Combine(modelDir, "model.onnx")) ||
                                      File.Exists(Path.Combine(modelDir, "voices.bin")) ||
                                      Directory.Exists(Path.Combine(modelDir, "espeak-ng-data")) ||
                                      Directory.EnumerateFiles(modelDir, "*.onnx", SearchOption.TopDirectoryOnly).Any();
            if (!hasTopLevelSignals)
            {
                var subdirs = Directory.GetDirectories(modelDir);
                if (subdirs.Length == 1)
                    scanDir = subdirs[0];
            }

            bool hasTokens = File.Exists(Path.Combine(scanDir, "tokens.txt"));
            bool hasModel = File.Exists(Path.Combine(scanDir, "model.onnx")) ||
                Directory.EnumerateFiles(scanDir, "*.onnx", SearchOption.TopDirectoryOnly)
                    .Any(f =>
                    {
                        string file = Path.GetFileName(f).ToLowerInvariant();
                        return !file.StartsWith("model-steps", StringComparison.Ordinal) &&
                               !file.StartsWith("vocos", StringComparison.Ordinal) &&
                               !file.StartsWith("vocoder", StringComparison.Ordinal);
                    });
            bool hasVoices = File.Exists(Path.Combine(scanDir, "voices.bin"));

            bool hasMatchaModel = Directory.EnumerateFiles(scanDir, "model-steps*.onnx", SearchOption.TopDirectoryOnly).Any();
            bool hasMatchaVocoder = Directory.EnumerateFiles(scanDir, "vocos*.onnx", SearchOption.TopDirectoryOnly).Any() ||
                                   Directory.EnumerateFiles(scanDir, "vocoder*.onnx", SearchOption.TopDirectoryOnly).Any();

            if (hasMatchaModel || hasMatchaVocoder)
            {
                if (!hasMatchaModel) return "Matcha model-steps*.onnx missing";
                if (!hasMatchaVocoder) return "Matcha vocoder/vocos ONNX missing";
                if (!hasTokens) return "Matcha tokens.txt missing";
                return null;
            }

            if (hasVoices || modelDir.Contains("kokoro", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasModel) return "Kokoro model.onnx missing";
                if (!hasVoices) return "Kokoro voices.bin missing";
                if (!hasTokens) return "Kokoro tokens.txt missing";
                return null;
            }

            if (hasModel || hasTokens)
            {
                if (!hasModel) return "model.onnx missing";
                if (!hasTokens) return "tokens.txt missing";
                return null;
            }

            return "No recognizable Sherpa model files found";
        }

        private sealed class PersistentTokenMetadata
        {
            public string FriendlyName { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Locale { get; set; } = "en-US";
            public string LanguageHexChain { get; set; } = "0409";
            public string Gender { get; set; } = "Neutral";
            public int ModelType { get; set; }
            public string ModelPath { get; set; } = "";
            public string TokensPath { get; set; } = "";
            public string DataDir { get; set; } = "";
            public string VoicesPath { get; set; } = "";
            public string AcousticModel { get; set; } = "";
            public string Vocoder { get; set; } = "";
            public int SampleRate { get; set; } = 22050;
            public int SpeakerCount { get; set; } = 1;
            public string ModelName { get; set; } = "";
        }

        private sealed class TokenSyncResult
        {
            public int Added { get; set; }
            public int Updated { get; set; }
            public int Removed { get; set; }
            public string? Error { get; set; }
            public string RegistryScope { get; set; } = "none";
            public List<string> Warnings { get; } = new List<string>();
        }

        private static TokenSyncResult SyncPersistentSherpaTokens(ModelScanResult scanResult)
        {
            var result = new TokenSyncResult();
            Dictionary<string, SherpaModelInfo> catalog;
            HashSet<string> validModelIds;
            try
            {
                catalog = LoadCatalogById();
                validModelIds = new HashSet<string>(
                    Directory.GetDirectories(ModelsDir)
                        .Select(Path.GetFileName)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Cast<string>(),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var issue in scanResult.Issues)
                    validModelIds.Remove(issue.ModelId);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }

            bool synced = false;
            try
            {
                using RegistryKey? hklmRoot = Registry.LocalMachine.CreateSubKey(SapiTokensRoot, writable: true);
                if (hklmRoot != null)
                {
                    result.RegistryScope = "HKLM";
                    SyncPersistentTokensToRoot(hklmRoot, validModelIds, catalog, result);
                    synced = true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Warnings.Add("HKLM token write denied (Administrator required only for machine-wide registration).");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"HKLM token sync failed: {ex.Message}");
            }

            if (!synced)
            {
                try
                {
                    using RegistryKey? hkcuRoot = Registry.CurrentUser.CreateSubKey(SapiTokensRoot, writable: true);
                    if (hkcuRoot == null)
                    {
                        result.Error = "Cannot open HKLM or HKCU SAPI token roots for writing.";
                        result.RegistryScope = "none";
                        return result;
                    }

                    result.RegistryScope = "HKCU";
                    SyncPersistentTokensToRoot(hkcuRoot, validModelIds, catalog, result);
                    result.Warnings.Add("Using per-user HKCU token registration fallback.");
                    if (Registry.LocalMachine.OpenSubKey(TokenEnumKeyPath, writable: false) != null)
                    {
                        result.Warnings.Add("HKLM TokenEnums is present. Some SAPI clients enumerate HKLM only and may miss HKCU-only voices.");
                    }
                    synced = true;
                }
                catch (Exception ex)
                {
                    result.Error = $"HKLM and HKCU token sync failed: {ex.Message}";
                    result.RegistryScope = "none";
                }
            }

            if (synced)
            {
                EnsurePersistentTokenMode(result);
            }
            return result;
        }

        private static void EnsurePersistentTokenMode(TokenSyncResult result)
        {
            bool machineWideTokens = string.Equals(result.RegistryScope, "HKLM", StringComparison.OrdinalIgnoreCase);
            try
            {
                using RegistryKey? enumCfg = Registry.CurrentUser.CreateSubKey(EnumeratorConfigKeyPath, writable: true);
                if (enumCfg != null)
                {
                    object? current = enumCfg.GetValue("NoSherpaVoices");
                    int currentValue = current is int i ? i : 0;
                    int desired = machineWideTokens ? 1 : 0;
                    if (currentValue != desired)
                    {
                        enumCfg.SetValue("NoSherpaVoices", desired, RegistryValueKind.DWord);
                        if (desired == 1)
                            result.Warnings.Add("Set Enumerator\\NoSherpaVoices=1 to prevent duplicate Sherpa voice enumeration.");
                        else
                            result.Warnings.Add("Set Enumerator\\NoSherpaVoices=0 because tokens are per-user (HKCU).");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to enforce Enumerator\\NoSherpaVoices mode: {ex.Message}");
            }

            try
            {
                bool hklmTokenEnumExists = Registry.LocalMachine.OpenSubKey(TokenEnumKeyPath, writable: false) != null;
                if (machineWideTokens && hklmTokenEnumExists)
                {
                    using RegistryKey? hkcuSpeech = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Speech\Voices\TokenEnums", writable: true);
                    if (hkcuSpeech != null)
                    {
                        using RegistryKey? hkcuEnum = hkcuSpeech.OpenSubKey("VoiceGardenEnumerator", writable: false);
                        if (hkcuEnum != null)
                        {
                            try
                            {
                                hkcuSpeech.DeleteSubKeyTree("VoiceGardenEnumerator", throwOnMissingSubKey: false);
                                result.Warnings.Add("Removed HKCU TokenEnums\\VoiceGardenEnumerator to avoid duplicate enumerator registration.");
                            }
                            catch
                            {
                                // non-fatal; keep silent to avoid noisy output on every sync
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to reconcile HKCU/HKLM TokenEnums: {ex.Message}");
            }
        }

        private static void SyncPersistentTokensToRoot(
            RegistryKey tokensRoot,
            HashSet<string> validModelIds,
            Dictionary<string, SherpaModelInfo> catalog,
            TokenSyncResult result)
        {
            foreach (string modelId in validModelIds.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                string modelDir = Path.Combine(ModelsDir, modelId);
                if (!TryBuildPersistentTokenMetadata(modelId, modelDir, catalog, out var meta, out string? why))
                {
                    if (!string.IsNullOrWhiteSpace(why))
                    {
                        result.Warnings.Add($"{modelId}: {why}");
                    }
                    continue;
                }

                string tokenName = "Sherpa-" + modelId;
                bool existed = tokensRoot.OpenSubKey(tokenName) != null;
                WritePersistentToken(tokensRoot, tokenName, meta);
                if (existed) result.Updated++;
                else result.Added++;

                if (ShouldCreateEnUsCompatibilityAlias(IsEnUsCompatibilityAliasEnabled(modelId), meta))
                {
                    string aliasTokenName = tokenName + CompatibilityAliasSuffix;
                    bool aliasExisted = tokensRoot.OpenSubKey(aliasTokenName) != null;
                    var aliasMeta = CloneAsEnUsAlias(meta);
                    WritePersistentToken(tokensRoot, aliasTokenName, aliasMeta, compatibilityAlias: true);
                    if (aliasExisted) result.Updated++;
                    else result.Added++;
                }
            }

            // Remove stale Sherpa-* tokens that no longer have a valid local model.
            foreach (string subName in tokensRoot.GetSubKeyNames())
            {
                if (!subName.StartsWith("Sherpa-", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryResolveTokenModelId(tokensRoot, subName, out string modelId, out bool isCompatAlias))
                    continue;

                if (!validModelIds.Contains(modelId))
                {
                    try
                    {
                        tokensRoot.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                        result.Removed++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"{subName}: remove failed ({ex.Message})");
                    }
                    continue;
                }

                if (isCompatAlias)
                {
                    bool keepAlias = false;
                    if (TryBuildPersistentTokenMetadata(modelId, Path.Combine(ModelsDir, modelId), catalog, out var meta, out _))
                        keepAlias = ShouldCreateEnUsCompatibilityAlias(IsEnUsCompatibilityAliasEnabled(modelId), meta);

                    if (keepAlias)
                        continue;
                }
                else
                {
                    continue;
                }

                try
                {
                    tokensRoot.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                    result.Removed++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{subName}: remove failed ({ex.Message})");
                }
            }
        }

        private static bool ShouldCreateEnUsCompatibilityAlias(bool requested, PersistentTokenMetadata meta)
        {
            return requested && !string.Equals(meta.Locale, "en-US", StringComparison.OrdinalIgnoreCase);
        }

        private static PersistentTokenMetadata CloneAsEnUsAlias(PersistentTokenMetadata meta)
        {
            return new PersistentTokenMetadata
            {
                FriendlyName = $"{meta.FriendlyName} (en-US alias)",
                DisplayName = $"{meta.DisplayName} (en-US alias)",
                Locale = "en-US",
                LanguageHexChain = "409",
                Gender = meta.Gender,
                ModelType = meta.ModelType,
                ModelPath = meta.ModelPath,
                TokensPath = meta.TokensPath,
                DataDir = meta.DataDir,
                VoicesPath = meta.VoicesPath,
                AcousticModel = meta.AcousticModel,
                Vocoder = meta.Vocoder,
                SampleRate = meta.SampleRate,
                SpeakerCount = meta.SpeakerCount,
                ModelName = meta.ModelName
            };
        }

        private static bool TryResolveTokenModelId(RegistryKey tokensRoot, string tokenName, out string modelId, out bool isCompatAlias)
        {
            modelId = string.Empty;
            isCompatAlias = false;

            try
            {
                using RegistryKey? tokenKey = tokensRoot.OpenSubKey(tokenName, writable: false);
                if (tokenKey != null)
                {
                    object? compat = tokenKey.GetValue("SherpaCompatAlias");
                    if (compat is int i && i != 0)
                        isCompatAlias = true;

                    using RegistryKey? attrs = tokenKey.OpenSubKey("Attributes", writable: false);
                    string? fromAttrs = attrs?.GetValue("SherpaModelName") as string;
                    if (!string.IsNullOrWhiteSpace(fromAttrs))
                        modelId = fromAttrs.Trim();
                }
            }
            catch
            {
                // Fall back to token-name parsing.
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                if (!tokenName.StartsWith("Sherpa-", StringComparison.OrdinalIgnoreCase))
                    return false;

                modelId = tokenName.Substring("Sherpa-".Length);
                if (modelId.EndsWith(CompatibilityAliasSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    modelId = modelId.Substring(0, modelId.Length - CompatibilityAliasSuffix.Length);
                    isCompatAlias = true;
                }
            }

            return !string.IsNullOrWhiteSpace(modelId);
        }

        private static bool IsEnUsCompatibilityAliasEnabled(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return false;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SherpaCompatKeyPath, writable: false);
                object? value = key?.GetValue(modelId);
                if (value is int i)
                    return i != 0;
                if (value is string s && int.TryParse(s, out int parsed))
                    return parsed != 0;
            }
            catch
            {
            }

            return false;
        }

        private static void SetEnUsCompatibilityAliasEnabled(string modelId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return;

            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(SherpaCompatKeyPath, writable: true);
            if (key == null)
                return;

            if (enabled)
                key.SetValue(modelId, 1, RegistryValueKind.DWord);
            else
                key.DeleteValue(modelId, throwOnMissingValue: false);
        }

        private static Dictionary<string, SherpaModelInfo> LoadCatalogById()
        {
            var byId = new Dictionary<string, SherpaModelInfo>(StringComparer.OrdinalIgnoreCase);
            string? path = FindCatalogPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return byId;

            string json = File.ReadAllText(path);
            var catalog = JsonSerializer.Deserialize<SherpaModelsCatalog>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (catalog == null)
                return byId;

            foreach (var kv in catalog)
            {
                string id = kv.Value.id ?? kv.Key;
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                byId[id] = kv.Value;
            }
            return byId;
        }

        private static bool TryBuildPersistentTokenMetadata(
            string modelId,
            string modelDir,
            Dictionary<string, SherpaModelInfo> catalogById,
            out PersistentTokenMetadata meta,
            out string? error)
        {
            meta = new PersistentTokenMetadata();
            error = null;

            string scanDir = ResolveModelScanDir(modelDir);
            string? modelPath = FindPrimaryModelOnnx(scanDir);
            string tokensPath = Path.Combine(scanDir, "tokens.txt");
            string dataDir = Path.Combine(scanDir, "espeak-ng-data");
            string voicesPath = Path.Combine(scanDir, "voices.bin");
            string? acoustic = Directory.EnumerateFiles(scanDir, "model-steps*.onnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
            string? vocoder = Directory.EnumerateFiles(scanDir, "vocos*.onnx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                            ?? Directory.EnumerateFiles(scanDir, "vocoder*.onnx", SearchOption.TopDirectoryOnly).FirstOrDefault();

            bool isMatcha = !string.IsNullOrWhiteSpace(acoustic) || !string.IsNullOrWhiteSpace(vocoder);
            bool isKokoro = File.Exists(voicesPath) || modelId.Contains("kokoro", StringComparison.OrdinalIgnoreCase);
            int modelType = isMatcha ? 1 : (isKokoro ? 2 : 0);

            if (isMatcha)
            {
                if (string.IsNullOrWhiteSpace(acoustic) || string.IsNullOrWhiteSpace(vocoder) || !File.Exists(tokensPath))
                {
                    error = "Matcha model missing acoustic/vocoder/tokens files.";
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(tokensPath))
                {
                    error = "VITS/Kokoro model missing model.onnx or tokens.txt.";
                    return false;
                }
                if (isKokoro && !File.Exists(voicesPath))
                {
                    error = "Kokoro model missing voices.bin.";
                    return false;
                }
            }

            SherpaModelInfo? catalogModel = null;
            catalogById.TryGetValue(modelId, out catalogModel);
            string displayName = !string.IsNullOrWhiteSpace(catalogModel?.name) ? catalogModel!.name! : modelId;
            string locale = GetPrimaryLocale(catalogModel) ?? "en-US";
            string langHexChain = BuildSapiLanguageHexChain(locale);
            string gender = InferGenderFromText($"{modelId} {displayName}");
            string friendly = $"Sherpa {displayName}";

            meta.FriendlyName = friendly;
            meta.DisplayName = displayName;
            meta.Locale = locale;
            meta.LanguageHexChain = langHexChain;
            meta.Gender = gender;
            meta.ModelType = modelType;
            meta.ModelPath = modelPath ?? "";
            meta.TokensPath = tokensPath;
            meta.DataDir = Directory.Exists(dataDir) ? dataDir : "";
            meta.VoicesPath = isKokoro ? voicesPath : "";
            meta.AcousticModel = acoustic ?? "";
            meta.Vocoder = vocoder ?? "";
            int defaultSampleRate = modelId.StartsWith("mms_", StringComparison.OrdinalIgnoreCase) ? 16000 : 22050;
            meta.SampleRate = catalogModel?.sample_rate ?? defaultSampleRate;
            meta.SpeakerCount = 1;
            meta.ModelName = modelId;
            return true;
        }

        private static string ResolveModelScanDir(string modelDir)
        {
            bool hasTop = File.Exists(Path.Combine(modelDir, "tokens.txt")) ||
                          File.Exists(Path.Combine(modelDir, "model.onnx")) ||
                          File.Exists(Path.Combine(modelDir, "voices.bin")) ||
                          Directory.Exists(Path.Combine(modelDir, "espeak-ng-data")) ||
                          Directory.EnumerateFiles(modelDir, "*.onnx", SearchOption.TopDirectoryOnly).Any();
            if (hasTop)
                return modelDir;

            string[] subdirs = Directory.GetDirectories(modelDir);
            if (subdirs.Length == 1)
                return subdirs[0];

            return modelDir;
        }

        private static string? FindPrimaryModelOnnx(string dir)
        {
            if (File.Exists(Path.Combine(dir, "model.onnx")))
                return Path.Combine(dir, "model.onnx");
            foreach (string f in Directory.EnumerateFiles(dir, "*.onnx", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(f).ToLowerInvariant();
                if (name.StartsWith("model-steps", StringComparison.Ordinal) ||
                    name.StartsWith("vocos", StringComparison.Ordinal) ||
                    name.StartsWith("vocoder", StringComparison.Ordinal))
                    continue;
                return f;
            }
            return null;
        }

        private static string? GetPrimaryLocale(SherpaModelInfo? model)
        {
            if (model?.language == null || model.language.Count == 0)
                return null;
            foreach (var lang in model.language)
            {
                string? code = lang.lang_code ?? lang.IsoCodeAlt;
                if (!string.IsNullOrWhiteSpace(code))
                    return NormalizeLocaleCode(code);
            }
            return null;
        }

        private static string NormalizeLocaleCode(string code)
        {
            string raw = code.Trim().Replace('_', '-');
            if (string.IsNullOrWhiteSpace(raw))
                return "en-US";

            try
            {
                var ci = new CultureInfo(raw);
                if (!ci.IsNeutralCulture && !string.IsNullOrWhiteSpace(ci.Name))
                    return ci.Name;

                // For neutral language tags like "en", choose a valid specific culture.
                var specific = CultureInfo.CreateSpecificCulture(ci.Name);
                if (!string.IsNullOrWhiteSpace(specific.Name))
                    return specific.Name;
            }
            catch
            {
                // fall through
            }

            try
            {
                var specific = CultureInfo.CreateSpecificCulture(raw);
                if (!string.IsNullOrWhiteSpace(specific.Name))
                    return specific.Name;
            }
            catch
            {
                // fall through
            }

            return "en-US";
        }

        private static string BuildSapiLanguageHexChain(string locale)
        {
            try
            {
                var normalized = NormalizeLocaleCode(locale);
                var ci = new CultureInfo(normalized);
                var ids = new List<int> { ci.LCID };
                // Add parent only if it is a real language LCID (exclude invariant/neutral sentinel values).
                if (ci.Parent != null && ci.Parent.LCID != ci.LCID && ci.Parent.LCID >= 0x0400 && ci.Parent.LCID != 0x007F)
                    ids.Add(ci.Parent.LCID);
                if (!ids.Contains(0x0409))
                    ids.Add(0x0409);
                // SAPI tokens commonly use hex without leading zeros (e.g. 409, 809).
                return string.Join(";", ids.Distinct().Select(id => id.ToString("X")));
            }
            catch
            {
                return "409";
            }
        }

        private static string InferGenderFromText(string text)
        {
            string v = text.ToLowerInvariant();
            if (v.Contains("female") || v.Contains("woman") || v.Contains("girl"))
                return "Female";
            if (v.Contains("male") || v.Contains("man") || v.Contains("boy"))
                return "Male";
            return "Neutral";
        }

        private static void WritePersistentToken(RegistryKey tokensRoot, string tokenName, PersistentTokenMetadata m, bool compatibilityAlias = false)
        {
            using RegistryKey tokenKey = tokensRoot.CreateSubKey(tokenName, writable: true)!;
            tokenKey.SetValue("", m.FriendlyName, RegistryValueKind.String);
            tokenKey.SetValue("CLSID", "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}", RegistryValueKind.String);
            tokenKey.SetValue("SherpaCompatAlias", compatibilityAlias ? 1 : 0, RegistryValueKind.DWord);

            using (RegistryKey attrs = tokenKey.CreateSubKey("Attributes", writable: true)!)
            {
                attrs.SetValue("Name", m.DisplayName, RegistryValueKind.String);
                attrs.SetValue("Gender", m.Gender, RegistryValueKind.String);
                attrs.SetValue("Age", "Adult", RegistryValueKind.String);
                attrs.SetValue("Language", m.LanguageHexChain, RegistryValueKind.String);
                attrs.SetValue("Locale", m.Locale, RegistryValueKind.String);
                attrs.SetValue("Vendor", "K2FSA", RegistryValueKind.String);
                attrs.SetValue("VoiceGardenType", "Sherpa;Offline", RegistryValueKind.String);
                attrs.SetValue("SherpaModelName", m.ModelName, RegistryValueKind.String);
            }

            using (RegistryKey cfg = tokenKey.CreateSubKey("VoiceGardenConfig", writable: true)!)
            {
                cfg.SetValue("EngineType", "Sherpa", RegistryValueKind.String);
                cfg.SetValue("SherpaOnnxModelType", m.ModelType, RegistryValueKind.DWord);
                cfg.SetValue("SampleRate", m.SampleRate, RegistryValueKind.DWord);
                cfg.SetValue("SpeakerCount", m.SpeakerCount, RegistryValueKind.DWord);
                cfg.SetValue("IsSherpaVoice", 1, RegistryValueKind.DWord);

                if (!string.IsNullOrWhiteSpace(m.ModelPath))
                    cfg.SetValue("SherpaOnnxModelPath", m.ModelPath, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(m.TokensPath))
                    cfg.SetValue("SherpaOnnxTokens", m.TokensPath, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(m.DataDir))
                    cfg.SetValue("SherpaOnnxDataDir", m.DataDir, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(m.VoicesPath))
                    cfg.SetValue("SherpaOnnxVoices", m.VoicesPath, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(m.AcousticModel))
                    cfg.SetValue("SherpaOnnxAcousticModel", m.AcousticModel, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(m.Vocoder))
                    cfg.SetValue("SherpaOnnxVocoder", m.Vocoder, RegistryValueKind.String);
            }
        }
    }

    // Model catalog classes
    public class SherpaModelsCatalog : Dictionary<string, SherpaModelInfo> { }

    public class SherpaModelInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("model_type")]
        public string? model_type { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("language")]
        public List<SherpaLanguage>? language { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sample_rate")]
        public int? sample_rate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? url { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("url_")]
        public string? url_ { get; set; }

        // Note: filesize_MB is handled by the same property due to case-insensitive deserialization
        [System.Text.Json.Serialization.JsonPropertyName("filesize_mb")]
        public double? filesize_mb { get; set; }
    }

    public class SherpaLanguage
    {
        [System.Text.Json.Serialization.JsonPropertyName("lang_code")]
        public string? lang_code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Iso Code")]
        public string? IsoCodeAlt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("language_name")]
        public string? language_name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Language Name")]
        public string? LanguageNameAlt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("country")]
        public string? country { get; set; }
    }

    public class VoiceInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Language { get; set; } = "";
        public string EngineType { get; set; } = "";
        public bool IsOffline { get; set; }
        public string ModelUrl { get; set; } = "";
        public double ModelSize { get; set; }
        public int SampleRate { get; set; }
        public string ModelType { get; set; } = "";
        public string Source { get; set; } = "";

        public bool IsDownloaded()
        {
            string modelsDir = Path.Combine(AppDataLayout.ModelsDir, Id);
            if (!Directory.Exists(modelsDir))
                return false;

            try
            {
                return Directory.EnumerateFiles(modelsDir, "*", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }
    }

    public class ModelScanIssue
    {
        public string ModelId { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class HuggingFaceTreeEntry
    {
        public string? type { get; set; }
        public string? path { get; set; }
        public long? size { get; set; }
    }

    public class ModelScanResult
    {
        public int TotalDirectories { get; set; }
        public int ValidModels { get; set; }
        public List<ModelScanIssue> Issues { get; set; } = new List<ModelScanIssue>();
    }
}
