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
        private RichTextBox? outputTextBox;
        private TextBox? testTextInput;
        private ProgressBar? progressBar;
        private BackgroundWorker? downloadWorker;

        private SherpaModelsCatalog? sherpaCatalog = null;
        private static readonly string AppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string OpenSpeechDir = Path.Combine(AppDataPath, "OpenSpeech");
        private static readonly string ModelsDir = Path.Combine(OpenSpeechDir, "models");

        // Voice list for CLI access
        public static List<VoiceInfo> AllVoices { get; private set; } = new List<VoiceInfo>();

        public MainForm()
        {
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
            languageComboBox.Items.Add("All Languages");
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
                Text = "Select a language and model to download. Models are cached in %LOCALAPPDATA%\\OpenSpeech\\models\\",
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

            voiceGroup.Controls.Add(voiceLabel);
            voiceGroup.Controls.Add(voiceComboBox);
            voiceGroup.Controls.Add(downloadButton);
            voiceGroup.Controls.Add(modelInfoLabel);
            voiceGroup.Controls.Add(progressBar);

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
                Size = new Size(720, 50),
                Text = "Actions"
            };

            openModelsFolderButton = new Button
            {
                Location = new Point(15, 20),
                Size = new Size(200, 30),
                Text = "Open Models Folder",
                FlatStyle = FlatStyle.Flat
            };
            openModelsFolderButton.Click += OpenModelsFolderButton_Click;

            Label actionsHint = new Label
            {
                Location = new Point(230, 25),
                Size = new Size(475, 25),
                Text = "Opens the folder where downloaded models are stored. You can also manually place model files here.",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8F)
            };

            actionsGroup.Controls.Add(openModelsFolderButton);
            actionsGroup.Controls.Add(actionsHint);

            // Output
            outputTextBox = new RichTextBox
            {
                Location = new Point(20, 415),
                Size = new Size(720, 280),
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
                       "  SherpaOnnxConfig.exe downloaded\r\n\r\n"
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
        }

        private HashSet<string> allLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<VoiceInfo>> voicesByLanguage = new Dictionary<string, List<VoiceInfo>>(StringComparer.OrdinalIgnoreCase);

        private async void LoadCatalogsAsync()
        {
            // Debug: Write to file immediately when method is called
            try { File.WriteAllText("sherpa_debug_start.txt", $"LoadCatalogsAsync called at {DateTime.Now}\n"); } catch { }

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
                    try { File.AppendAllText("sherpa_debug_start.txt", $"Checking path: {catalogPath}, exists: {File.Exists(catalogPath)}\n"); } catch { }
                    if (File.Exists(catalogPath))
                    {
                        catalogContent = await File.ReadAllTextAsync(catalogPath);
                        AppendOutput($"Loaded catalog from: {catalogPath}", Color.FromArgb(100, 255, 100));
                        try { File.AppendAllText("sherpa_debug_start.txt", $"Catalog loaded, length: {catalogContent?.Length ?? 0}\n"); } catch { }
                        break;
                    }
                }

                if (string.IsNullOrEmpty(catalogContent))
                {
                    AppendOutput("WARNING: merged_models.json not found. Models will need to be added manually.", Color.FromArgb(255, 200, 100));
                    statusLabel!.Text = "Status: No catalog found";
                    languageComboBox!.Items.Add("All Languages");
                    languageComboBox.SelectedIndex = 0;
                    return;
                }

                sherpaCatalog = JsonSerializer.Deserialize<SherpaModelsCatalog>(catalogContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                AppendOutput($"Loaded {sherpaCatalog?.Count ?? 0} SherpaOnnx models from catalog.", Color.FromArgb(100, 255, 100));

                // Debug output - write to the same file
                try { File.AppendAllText("sherpa_debug_start.txt", $"Catalog deserialized, count: {sherpaCatalog?.Count ?? 0}\n"); } catch { }

                // Try to access first model with error handling
                try
                {
                    if (sherpaCatalog != null && sherpaCatalog.Count > 0)
                    {
                        try { File.AppendAllText("sherpa_debug_start.txt", "About to call First()\n"); } catch { }
                        var first = sherpaCatalog.First();
                        try { File.AppendAllText("sherpa_debug_start.txt", $"First().Value: {first.Value?.id ?? "null"}\n"); } catch { }
                        var firstModel = first.Value;
                        try { File.AppendAllText("sherpa_debug_start.txt", $"firstModel.language is null: {firstModel.language == null}\n"); } catch { }
                    }
                    else
                    {
                        try { File.AppendAllText("sherpa_debug_start.txt", "Catalog is null or empty\n"); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText("sherpa_debug_start.txt", $"Error accessing first model: {ex.Message}\n"); } catch { }
                }

                // Debug: Check first model
                if (sherpaCatalog != null && sherpaCatalog.Count > 0)
                {
                    var firstModel = sherpaCatalog.First().Value;
                    AppendOutput($"DEBUG: First model ID: {firstModel.id}", Color.FromArgb(150, 150, 255));
                    AppendOutput($"DEBUG: First model.language is null: {firstModel.language == null}", Color.FromArgb(150, 150, 255));
                    if (firstModel.language != null && firstModel.language.Count > 0)
                    {
                        AppendOutput($"DEBUG: First language name: {firstModel.language[0].language_name}", Color.FromArgb(150, 150, 255));
                    }
                }

                // Process models into language groups
                AllVoices.Clear();
                voicesByLanguage.Clear();
                allLanguages.Clear();
                allLanguages.Add("All Languages");

                if (sherpaCatalog != null)
                {
                    int skippedCount = 0;
                    int processedCount = 0;

                    foreach (var kvp in sherpaCatalog)
                    {
                        try
                        {
                            var model = kvp.Value;
                            string langStr = "Unknown";

                            // Debug: Check if language property is null
                            if (model.language == null || model.language.Count == 0)
                            {
                                skippedCount++;
                                continue;
                            }

                            // Get language name
                            langStr = model.language[0].language_name ?? "Unknown";

                            // Skip if language detection failed
                            if (langStr == "Unknown" || langStr.StartsWith("Unknown language"))
                            {
                                skippedCount++;
                                continue;
                            }

                            // Add to language set
                            if (!allLanguages.Contains(langStr))
                                allLanguages.Add(langStr);

                            // Create voice info
                            string engineType = model.id?.Contains("mms") == true ? "MMS" :
                                              model.id?.Contains("kokoro") == true ? "Kokoro" :
                                              model.id?.Contains("vits") == true ? "VITS" :
                                              model.id?.Contains("matcha") == true ? "Matcha-TTS" : "SherpaOnnx";

                            var voice = new VoiceInfo
                            {
                                Id = model.id ?? kvp.Key,
                                Name = model.name ?? "Unknown",
                                Language = langStr,
                                EngineType = engineType,
                                IsOffline = true,
                                ModelUrl = model.url ?? model.url_ ?? "",
                                ModelSize = model.filesize_mb ?? 0,
                                SampleRate = model.sample_rate ?? 22050,
                                ModelType = model.model_type ?? "vits",
                                Source = "sherpa"
                            };

                            AllVoices.Add(voice);

                            // Group by language
                            if (!voicesByLanguage.ContainsKey(langStr))
                                voicesByLanguage[langStr] = new List<VoiceInfo>();
                            voicesByLanguage[langStr].Add(voice);

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
                UpdateVoiceList(languageComboBox.SelectedItem.ToString() ?? "All Languages");
            }
        }

        private void UpdateVoiceList(string language)
        {
            voiceComboBox!.Items.Clear();
            voiceComboBox.SelectedIndex = -1;

            IEnumerable<VoiceInfo> voicesToShow;

            if (language == "All Languages")
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
                    UpdateVoiceList(languageComboBox!.SelectedItem?.ToString() ?? "All Languages");
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
        }

        private void DownloadWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            progressBar!.Visible = false;
        }

        private void DownloadTarArchive(VoiceInfo voice, string modelDir)
        {
            string tarFile = Path.Combine(modelDir, "model.tar.bz2");

            this.Invoke((Action)(() =>
                AppendOutput($"Downloading from {voice.ModelUrl}...", Color.FromArgb(150, 200, 255))));

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(30);
                var response = client.GetAsync(voice.ModelUrl).Result;
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? 0;
                long downloadedBytes = 0;

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
                            int progress = 10 + (int)((downloadedBytes * 80) / totalBytes);
                            downloadWorker!.ReportProgress(progress);
                        }
                    }
                }
            }

            downloadWorker!.ReportProgress(90);
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
                Type spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (spVoiceType == null)
                {
                    AppendOutput("\rSAPI5 not available on this system.", Color.FromArgb(255, 200, 100));
                    return;
                }

                dynamic voiceObj = Activator.CreateInstance(spVoiceType);

                // Find the SherpaOnnx voice by name
                var voices = voiceObj.GetVoices();
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
                string catalogPath = FindCatalogPath();
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
                    string langStr = "Unknown";

                    if (model.language != null && model.language.Count > 0)
                    {
                        langStr = model.language[0].language_name ?? "Unknown";
                    }

                    if (langStr == "Unknown" || langStr.StartsWith("Unknown language"))
                        continue;

                    if (!langGroups.ContainsKey(langStr))
                        langGroups[langStr] = new List<SherpaModelInfo>();

                    // Apply language filter if specified
                    if (string.IsNullOrEmpty(languageFilter) ||
                        langStr.Equals(languageFilter, StringComparison.OrdinalIgnoreCase) ||
                        langStr.Contains(languageFilter, StringComparison.OrdinalIgnoreCase))
                    {
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

                        Console.WriteLine($"  {model.id,-40} {model.name,-30} [{size}] [{status}]");
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
                string catalogPath = FindCatalogPath();
                if (string.IsNullOrEmpty(catalogPath))
                {
                    Console.WriteLine("ERROR: merged_models.json not found!");
                    return 1;
                }

                string json = File.ReadAllText(catalogPath);
                var catalog = JsonSerializer.Deserialize<SherpaModelsCatalog>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
            return Directory.Exists(modelDir) && Directory.GetFiles(modelDir).Length > 0;
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
                "OpenSpeech", "models", Id);
            return Directory.Exists(modelsDir) && Directory.GetFiles(modelsDir).Length > 0;
        }
    }
}
