using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows.Forms;

static class Program
{
    sealed class Branding
    {
        public string ProductName { get; set; } = "VoiceGardenSAPIAdapter";
        public string SetupCaption { get; set; } = "VoiceGardenSAPIAdapter Setup";
        public string InstallFolderName { get; set; } = "VoiceGardenSAPIAdapter";
    }

    static int Main(string[] args)
    {
        try
        {
            Branding branding = LoadBranding(AppContext.BaseDirectory);

            bool uninstall = HasArg(args, "--uninstall") || HasArg(args, "/uninstall");
            bool quiet = HasArg(args, "--silent") || HasArg(args, "/silent") || HasArg(args, "/quiet");
            bool removeAppData = HasArg(args, "--remove-appdata");

            string baseDir = AppContext.BaseDirectory;
            string msiPath = Path.Combine(baseDir, "VoiceGardenSAPIAdapter.msi");

            if (!File.Exists(msiPath))
            {
                Notify($"MSI not found next to setup.exe:\n{msiPath}", branding.SetupCaption, quiet, error: true);
                return 2;
            }

            string msiexecArgs;
            if (uninstall)
            {
                string? productCode = FindInstalledProductCode(branding.ProductName);
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    Notify($"Could not find an installed {branding.ProductName} product.", branding.SetupCaption, quiet, error: true);
                    return 3;
                }

                msiexecArgs = $"/x {productCode}";
                if (quiet)
                    msiexecArgs += " /qn";
                else
                    msiexecArgs += " /passive";
                if (removeAppData)
                    msiexecArgs += " REMOVE_APPDATA=1";
            }
            else
            {
                msiexecArgs = $"/i \"{msiPath}\"";
                if (quiet)
                    msiexecArgs += " /qn";
                else
                    msiexecArgs += " /passive";
            }

            int rc = RunMsiexec(msiexecArgs);
            if (!quiet)
            {
                if (rc == 0)
                {
                    if (uninstall)
                    {
                        Notify("Uninstall completed.", branding.SetupCaption, quiet, error: false);
                    }
                    else
                    {
                        if (!TryLaunchInstalledInstaller(branding))
                        {
                            Notify("Install completed, but Installer.exe was not found in the install location.", branding.SetupCaption, quiet, error: true);
                            return 4;
                        }
                    }
                }
                else if (rc == 3010)
                {
                    Notify("Operation completed. A reboot is required.", branding.SetupCaption, quiet, error: false);
                }
                else if (rc == 1618)
                {
                    Notify("Another installer is currently running. Please wait and try again.", branding.SetupCaption, quiet, error: true);
                }
                else
                {
                    Notify($"Setup failed with exit code {rc}.", branding.SetupCaption, quiet, error: true);
                }
            }
            return rc;
        }
        catch (Exception ex)
        {
            Notify(ex.Message, "Setup", quiet: false, error: true);
            return 1;
        }
    }

    static Branding LoadBranding(string baseDir)
    {
        try
        {
            string p = Path.Combine(baseDir, "branding.json");
            if (!File.Exists(p))
                return new Branding();

            using FileStream fs = File.OpenRead(p);
            using JsonDocument doc = JsonDocument.Parse(fs);
            Branding b = new Branding();
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("product_name", out JsonElement product) && product.ValueKind == JsonValueKind.String)
                b.ProductName = product.GetString() ?? b.ProductName;
            if (root.TryGetProperty("app_caption", out JsonElement caption) && caption.ValueKind == JsonValueKind.String)
                b.SetupCaption = caption.GetString() ?? b.SetupCaption;
            if (root.TryGetProperty("install_folder_name", out JsonElement folder) && folder.ValueKind == JsonValueKind.String)
                b.InstallFolderName = folder.GetString() ?? b.InstallFolderName;
            return b;
        }
        catch
        {
            return new Branding();
        }
    }

    static bool HasArg(string[] args, string needle) =>
        args.Any(a => string.Equals(a, needle, StringComparison.OrdinalIgnoreCase));

    static int RunMsiexec(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = arguments,
            UseShellExecute = true
        };

        using Process? p = Process.Start(psi);
        if (p == null)
            return 1;
        p.WaitForExit();
        return p.ExitCode;
    }

    static string? FindInstalledProductCode(string displayName)
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) continue;

            foreach (string sub in uninstall.GetSubKeyNames())
            {
                using RegistryKey? key = uninstall.OpenSubKey(sub);
                if (key == null) continue;
                string? dn = key.GetValue("DisplayName") as string;
                if (!string.Equals(dn, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // MSI uninstall entries normally store a GUID key name or an UninstallString containing one.
                if (IsProductCode(sub))
                    return sub;

                string? uninstallString = key.GetValue("UninstallString") as string;
                if (!string.IsNullOrWhiteSpace(uninstallString))
                {
                    Match m = Regex.Match(uninstallString, @"\{[0-9A-Fa-f\-]{36}\}");
                    if (m.Success)
                        return m.Value;
                }
            }
        }
        return null;
    }

    static bool IsProductCode(string value) =>
        Regex.IsMatch(value, @"^\{[0-9A-Fa-f\-]{36}\}$");

    static bool TryLaunchInstalledInstaller(Branding branding)
    {
        // Prefer VoiceGarden.UI.exe (Avalonia), fall back to Installer.exe (C++)
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), branding.InstallFolderName, "VoiceGarden.UI.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), branding.InstallFolderName, "VoiceGarden.UI.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), branding.InstallFolderName, "Installer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), branding.InstallFolderName, "Installer.exe")
        };

        foreach (string p in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(p))
                continue;

            Process.Start(new ProcessStartInfo
            {
                FileName = p,
                UseShellExecute = true
            });
            return true;
        }

        return false;
    }

    static void Notify(string message, string caption, bool quiet, bool error)
    {
        if (quiet)
            return;
        MessageBox.Show(
            message,
            caption,
            MessageBoxButtons.OK,
            error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }
}
