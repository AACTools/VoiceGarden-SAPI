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
using System.Text.RegularExpressions;

namespace SherpaOnnxConfig
{
    public partial class MainForm : Form
    {
        private Label? statusLabel;
        private ComboBox? languageComboBox;
        private ComboBox? voiceComboBox;
        private Button? downloadButton;
        private Button? testVoiceButton;
        private Button? openModelsFolderButton;
        private Button? rescanModelsButton;
        private RichTextBox? outputTextBox;
        private TextBox? testTextInput;
        private ProgressBar? progressBar;
        private Label? downloadProgressLabel;
        private BackgroundWorker? downloadWorker;

        private SherpaModelsCatalog? sherpaCatalog = null;
        private static readonly string AppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string AdapterDataDir = Path.Combine(AppDataPath, "NaturalVoiceSAPIAdapter");
        private static readonly string ModelsDir = Path.Combine(AdapterDataDir, "models");
        private static readonly string ScanErrorsPath = Path.Combine(AdapterDataDir, "sherpa_model_scan_errors.json");
        private const string AllLanguagesOption = "All Languages";
        private static readonly Regex LanguageCodeRegex = new Regex(@"(?:^|[-_])([a-z]{2})(?:[-_][A-Za-z]{2})?(?:[-_]|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Voice list for CLI access
        public static List<VoiceInfo> AllVoices { get; private set; } = new List<VoiceInfo>();

        private readonly bool autoRescanOnStartup;
        private static List<ModelScanIssue> s_lastScanIssues = new List<ModelScanIssue>();

        public MainForm(bool autoRescanOnStartup = false)
        {
            this.autoRescanOnStartup = autoRescanOnStartup;
            InitializeComponent();
            LoadCatalogsAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "NaturalVoice SAPI - SherpaOnnx Model Manager";
            this.Size = new Size(760, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(245, 245, 245);

            // Title
            Label titleLabel = new Label
            {
                Location = new Point(20, 15),
                Size = new Size(720, 25),
                Text = "SherpaOnnx Offline TTS Model Manager",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 51, 102)
            };

            // Status label
            statusLabel = new Label
            {
                Location = new Point(20, 45),
                Size = new Size(720, 20),
                Text = "Status: Loading voice catalog...",
                ForeColor = Color.FromArgb(100, 100, 100)
            };

            // Language selection
            Label languageLabel = new Label
            {
                Location = new Point(20, 80),
                Size = new Size(120, 20),
                Text = "Language:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            languageComboBox = new ComboBox
            {
                Location = new Point(140, 78),
                Size = new Size(200, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            languageComboBox.Items.Add(AllLanguagesOption);
            languageComboBox.SelectedIndex = 0;
            languageComboBox.SelectedIndexChanged += LanguageComboBox_SelectedIndexChanged;

            // Voice selection
            GroupBox voiceGroup = new GroupBox
            {
                Location = new Point(20, 115),
                Size = new Size(720, 120),
                Text = "Available SherpaOnnx Models"
            };

            Label voiceLabel = new Label
            {
                Location = new Point(15, 25),
                Size = new Size(80, 20),
                Text = "Model:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            voiceComboBox = new ComboBox
            {
                Location = new Point(100, 23),
                Size = new Size(580, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            voiceComboBox.SelectedIndexChanged += VoiceComboBox_SelectedIndexChanged;

            downloadButton = new Button
            {
                Location = new Point(15, 55),
                Size = new Size(120, 30),
                Text = "Download Model",
                BackColor = Color.FromArgb(255, 140, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            downloadButton.FlatAppearance.BorderSize = 0;
            downloadButton.Click += DownloadButton_Click;

            Label modelInfoLabel = new Label
            {
                Location = new Point(15, 90),
                Size = new Size(690, 20),
                Text = "Select a language and model to download. Models are cached in %LOCALAPPDATA%\\NaturalVoiceSAPIAdapter\\models\\",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };

            progressBar = new ProgressBar
            {
                Location = new Point(300, 55),
                Size = new Size(280, 20),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };

            downloadProgressLabel = new Label
            {
                Location = new Point(300, 78),
                Size = new Size(390, 15),
                Text = "",
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 8F),
                Visible = false
            };

            voiceGroup.Controls.Add(voiceLabel);
            voiceGroup.Controls.Add(voiceComboBox);
            voiceGroup.Controls.Add(downloadButton);
            voiceGroup.Controls.Add(modelInfoLabel);
            voiceGroup.Controls.Add(progressBar);
            voiceGroup.Controls.Add(downloadProgressLabel);

            // Test group
            GroupBox testGroup = new GroupBox
            {
                Location = new Point(20, 245),
                Size = new Size(720, 100),
                Text = "Test Voice (After Download)"
            };

            testTextInput = new TextBox
            {
                Location = new Point(15, 25),
                Size = new Size(545, 25),
                Text = "The quick brown fox jumps over the lazy dog.",
                Font = new Font("Segoe UI", 9F)
            };

            testVoiceButton = new Button
            {
                Location = new Point(570, 23),
                Size = new Size(135, 30),
                Text = "▶ Test",
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            testVoiceButton.FlatAppearance.BorderSize = 0;
            testVoiceButton.Click += TestVoiceButton_Click;

            Label hintLabel = new Label
            {
                Location = new Point(15, 55),
                Size = new Size(690, 35),
                Text = "Tests the selected voice using SAPI5. The voice must be downloaded and the DLL registered first.",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };

            testGroup.Controls.Add(testTextInput);
            testGroup.Controls.Add(testVoiceButton);
            testGroup.Controls.Add(hintLabel);

            // Actions group
            GroupBox actionsGroup = new GroupBox
            {
                Location = new Point(20, 355),
                Size = new Size(720, 70),
                Text = "Actions"
            };

            openModelsFolderButton = new Button
            {
                Location = new Point(15, 20),
                Size = new Size(170, 30),
                Text = "Open Models Folder",
                FlatStyle = FlatStyle.Flat
            };
            openModelsFolderButton.Click += OpenModelsFolderButton_Click;

            rescanModelsButton = new Button
            {
                Location = new Point(195, 20),
                Size = new Size(150, 30),
                Text = "Rescan Models",
                FlatStyle = FlatStyle.Flat
            };
            rescanModelsButton.Click += RescanModelsButton_Click;

            Label actionsHint = new Label
            {
                Location = new Point(15, 52),
                Size = new Size(690, 15),
                Text = "Rescan validates local models and shows per-model errors used by SAPI token registration.",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };

            actionsGroup.Controls.Add(openModelsFolderButton);
            actionsGroup.Controls.Add(rescanModelsButton);
            actionsGroup.Controls.Add(actionsHint);

            // Output
            outputTextBox = new RichTextBox
            {
                Location = new Point(20, 435),
                Size = new Size(720, 260),
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
            this.Controls.Add(voiceGroup);
            this.Controls.Add(testGroup);
            this.Controls.Add(actionsGroup);
            this.Controls.Add(outputTextBox);

            this.Shown += (_, _) =>
            {
                if (autoRescanOnStartup)
                {
                    PerformLocalModelRescan();
                }
            };
        }

        private HashSet<string> allLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<VoiceInfo>> voicesByLanguage = new Dictionary<string, List<VoiceInfo>>(StringComparer.OrdinalIgnoreCase);

        private async void LoadCatalogsAsync()
        {
            try
            {
                statusLabel!.Text = "Status: Loading SherpaOnnx catalog...";

                // Try to find catalog in multiple locations
                string[] catalogPaths = new string[]
                {
                    Path.Combine(AppContext.BaseDirectory, "merged_models.json"),
                    Path.Combine(Application.StartupPath, "merged_models.json"),
                    "merged_models.json"
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
                    AppendOutput("WARNING: merged_models.json not found. Models will need to be added manually.", Color.FromArgb(255, 200, 100));
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
                languageComboBox!.Items.Clear();
                foreach (var lang in allLanguages.OrderBy(l => l))
                {
                    languageComboBox.Items.Add(lang);
                }
                languageComboBox.SelectedIndex = 0;

                // Show all voices initially
                UpdateVoiceList("All Languages");

                AppendOutput($"Found {allLanguages.Count - 1} unique languages with {AllVoices.Count} models.", Color.FromArgb(100, 200, 255));

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
            if (languageComboBox!.SelectedItem != null)
            {
                UpdateVoiceList(languageComboBox.SelectedItem.ToString() ?? AllLanguagesOption);
            }
        }

        private void UpdateVoiceList(string language)
        {
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

            foreach (var voice in voicesToShow.OrderBy(v => v.Name))
            {
                bool downloaded = voice.IsDownloaded();
                string status = downloaded ? "[✓]" : "[↓]";
                string size = voice.ModelSize > 0 ? $" ({voice.ModelSize:F0} MB)" : "";
                voiceComboBox.Items.Add($"{voice.Id} - {voice.Name}{size} [{voice.EngineType}] {status}");
            }

            if (voiceComboBox.Items.Count > 0)
                voiceComboBox.SelectedIndex = 0;

            statusLabel!.Text = $"Status: {voiceComboBox.Items.Count} model(s) available";
        }

        private void VoiceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var voice = GetSelectedVoice();
            if (voice != null)
            {
                downloadButton!.Enabled = !voice.IsDownloaded();
                downloadButton.Text = voice.IsDownloaded() ? "Downloaded" : "Download Model";
                downloadProgressLabel!.Visible = false;
            }
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
            var voice = GetSelectedVoice();
            if (voice == null) return;

            if (downloadWorker!.IsBusy)
            {
                AppendOutput("Download already in progress...", Color.FromArgb(255, 200, 100));
                return;
            }

            AppendOutput($"\r\n=== Downloading Model: {voice.Id} ===", Color.FromArgb(255, 140, 0));
            if (voice.ModelSize > 0)
                AppendOutput($"Size: {voice.ModelSize:F2} MB", Color.FromArgb(200, 200, 200));
            statusLabel!.Text = $"Status: Downloading {voice.Id}...";

            progressBar!.Visible = true;
            progressBar.Value = 0;
            downloadProgressLabel!.Visible = true;
            downloadProgressLabel.Text = "Preparing download...";
            downloadButton!.Enabled = false;
            voiceComboBox!.Enabled = false;
            languageComboBox!.Enabled = false;

            downloadWorker.RunWorkerAsync(voice);
        }

        private void DownloadWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            var voice = e.Argument as VoiceInfo;
            if (voice == null) return;

            try
            {
                string modelDir = Path.Combine(ModelsDir, voice.Id);
                Directory.CreateDirectory(modelDir);

                if (string.IsNullOrEmpty(voice.ModelUrl))
                {
                    this.Invoke((Action)(() =>
                        AppendOutput("ERROR: No download URL available", Color.FromArgb(255, 100, 100))));
                    return;
                }

                downloadWorker!.ReportProgress(10);

                if (voice.ModelUrl.EndsWith(".tar.bz2") || voice.ModelUrl.Contains("tar.bz2"))
                {
                    DownloadTarArchive(voice, modelDir);
                }
                else
                {
                    downloadWorker.ReportProgress(100);
                    this.Invoke((Action)(() =>
                        AppendOutput($"ERROR: Unknown URL format: {voice.ModelUrl}", Color.FromArgb(255, 100, 100))));
                    return;
                }

                downloadWorker.ReportProgress(100);
                this.Invoke((Action)(() =>
                {
                    AppendOutput($"\r✓ Model downloaded to {modelDir}", Color.FromArgb(100, 255, 100));
                    statusLabel!.Text = $"Status: {voice.Id} downloaded";
                    UpdateVoiceList(languageComboBox!.SelectedItem?.ToString() ?? AllLanguagesOption);
                }));
            }
            catch (Exception ex)
            {
                this.Invoke((Action)(() =>
                {
                    AppendOutput($"\rERROR: {ex.Message}", Color.FromArgb(255, 100, 100));
                    statusLabel!.Text = "Status: Download failed";
                }));
            }
        }

        private void DownloadWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            progressBar!.Value = e.ProgressPercentage;
            if (e.UserState is string message && !string.IsNullOrWhiteSpace(message))
            {
                downloadProgressLabel!.Visible = true;
                downloadProgressLabel.Text = message;
                statusLabel!.Text = $"Status: {message}";
            }
        }

        private void DownloadWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            progressBar!.Visible = false;
            downloadProgressLabel!.Visible = false;
            voiceComboBox!.Enabled = true;
            languageComboBox!.Enabled = true;
            VoiceComboBox_SelectedIndexChanged(null, EventArgs.Empty);
        }

        private void DownloadTarArchive(VoiceInfo voice, string modelDir)
        {
            string tarFile = Path.Combine(modelDir, "model.tar.bz2");

            this.Invoke((Action)(() =>
                AppendOutput($"Downloading from {voice.ModelUrl}...", Color.FromArgb(150, 200, 255))));
            downloadWorker!.ReportProgress(5, "Connecting to download source...");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(30);
                var response = client.GetAsync(voice.ModelUrl).Result;
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

            downloadWorker!.ReportProgress(90, $"Extracting {voice.Id}...");
            this.Invoke((Action)(() => AppendOutput("Extracting...", Color.FromArgb(150, 200, 255))));

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

            // Clean up tar file
            File.Delete(tarFile);
            downloadWorker!.ReportProgress(98, $"Finalizing {voice.Id}...");
        }

        private void TestVoiceButton_Click(object? sender, EventArgs e)
        {
            var voice = GetSelectedVoice();
            if (voice == null || !voice.IsDownloaded())
            {
                MessageBox.Show("Please download the model first.", "Model Not Downloaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string testText = testTextInput!.Text.Trim();
            if (string.IsNullOrEmpty(testText))
                testText = "The quick brown fox jumps over the lazy dog.";

            try
            {
                // Try to speak using SAPI5 via late binding
                Type? spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (spVoiceType == null)
                {
                    AppendOutput("\rSAPI5 not available on this system.", Color.FromArgb(255, 200, 100));
                    return;
                }

                object? voiceObjRaw = Activator.CreateInstance(spVoiceType);
                if (voiceObjRaw == null)
                {
                    AppendOutput("\rFailed to create SAPI.SpVoice instance.", Color.FromArgb(255, 200, 100));
                    return;
                }
                dynamic voiceObj = voiceObjRaw;

                // Find the SherpaOnnx voice by name
                var voices = voiceObj.GetVoices();
                if (voices == null)
                {
                    AppendOutput("\rNo SAPI voices collection returned.", Color.FromArgb(255, 200, 100));
                    return;
                }
                bool found = false;
                for (int i = 0; i < voices.Count; i++)
                {
                    var v = voices.Item(i);
                    if (v.Id != null && v.Id.Contains(voice.Id))
                    {
                        voiceObj.Voice = v;
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    voiceObj.Speak(testText);
                    AppendOutput($"\r✓ Played test using {voice.Id}", Color.FromArgb(100, 255, 100));
                }
                else
                {
                    AppendOutput($"\rVoice {voice.Id} not found in SAPI5. Please install and register the DLL first.", Color.FromArgb(255, 200, 100));
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"\rERROR testing voice: {ex.Message}", Color.FromArgb(255, 100, 100));
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

        private void PerformLocalModelRescan()
        {
            AppendOutput($"\r\n=== Rescanning local models in {ModelsDir} ===", Color.FromArgb(120, 200, 255));
            var result = ScanLocalModels();
            s_lastScanIssues = result.Issues;
            PersistLastScanIssues(result.Issues);

            if (result.TotalDirectories == 0)
            {
                AppendOutput("No local model directories found.", Color.FromArgb(255, 200, 100));
                return;
            }

            AppendOutput($"Valid models: {result.ValidModels}/{result.TotalDirectories}", Color.FromArgb(100, 255, 100));

            if (result.Issues.Count == 0)
            {
                AppendOutput("No scan errors detected.", Color.FromArgb(100, 255, 100));
                return;
            }

            AppendOutput($"Scan errors: {result.Issues.Count}", Color.FromArgb(255, 180, 100));
            foreach (var issue in result.Issues.OrderBy(i => i.ModelId))
            {
                AppendOutput($"  {issue.ModelId}: {issue.Error}", Color.FromArgb(255, 120, 120));
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
                    Console.WriteLine("ERROR: merged_models.json not found!");
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
                    Console.WriteLine("ERROR: merged_models.json not found!");
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
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(30);
                    var response = client.GetAsync(modelUrl).Result;
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

                    Console.WriteLine("\n✓ Model downloaded successfully!");
                    return 0;
                }
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

            return result.Issues.Count == 0 ? 0 : 2;
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
                Path.Combine(AppContext.BaseDirectory, "merged_models.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "merged_models.json"),
                "merged_models.json"
            };

            foreach (string path in paths)
            {
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private static bool IsModelDownloaded(string modelId)
        {
            string modelDir = Path.Combine(ModelsDir, modelId);
            if (!Directory.Exists(modelDir))
                return false;

            try
            {
                return Directory.EnumerateFiles(modelDir, "*", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
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

            string modelId = model.id ?? string.Empty;
            foreach (Match match in LanguageCodeRegex.Matches(modelId))
            {
                string langCode = match.Groups[1].Value.ToLowerInvariant();
                string? inferred = ResolveLanguageName(new SherpaLanguage { lang_code = langCode });
                if (!string.IsNullOrWhiteSpace(inferred))
                    names.Add(inferred);
            }

            if (names.Count > 0)
                return names;

            return new[] { "Unknown" };
        }

        private static string? ResolveLanguageName(SherpaLanguage? lang)
        {
            if (lang == null)
                return null;

            string? languageName = lang.language_name?.Trim();
            if (!string.IsNullOrWhiteSpace(languageName) &&
                !languageName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                !languageName.StartsWith("Unknown language", StringComparison.OrdinalIgnoreCase))
            {
                return languageName;
            }

            string? code = lang.lang_code?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return null;

            try
            {
                string normalized = code.ToLowerInvariant();
                var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                    .FirstOrDefault(c => c.TwoLetterISOLanguageName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
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
            bool hasTokens = File.Exists(Path.Combine(modelDir, "tokens.txt"));
            bool hasModel = File.Exists(Path.Combine(modelDir, "model.onnx"));
            bool hasVoices = File.Exists(Path.Combine(modelDir, "voices.bin"));

            bool hasMatchaModel = Directory.EnumerateFiles(modelDir, "model-steps*.onnx", SearchOption.TopDirectoryOnly).Any();
            bool hasMatchaVocoder = Directory.EnumerateFiles(modelDir, "vocos*.onnx", SearchOption.TopDirectoryOnly).Any() ||
                                   Directory.EnumerateFiles(modelDir, "vocoder*.onnx", SearchOption.TopDirectoryOnly).Any();

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

        [System.Text.Json.Serialization.JsonPropertyName("language_name")]
        public string? language_name { get; set; }

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
            string modelsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NaturalVoiceSAPIAdapter", "models", Id);
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

    public class ModelScanResult
    {
        public int TotalDirectories { get; set; }
        public int ValidModels { get; set; }
        public List<ModelScanIssue> Issues { get; set; } = new List<ModelScanIssue>();
    }
}
