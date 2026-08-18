using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class Program
{
    sealed class Branding
    {
        public const string ProductName = "VoiceGardenSAPI";
        public const string SetupCaption = "VoiceGardenSAPI Setup";
        public const string InstallFolderName = "VoiceGardenSAPI";
    }

    static int Main(string[] args)
    {
        try
        {
            bool uninstall = HasArg(args, "--uninstall") || HasArg(args, "/uninstall");
            bool quiet = HasArg(args, "--silent") || HasArg(args, "/silent") || HasArg(args, "/quiet");
            bool removeAppData = HasArg(args, "--remove-appdata");

            string baseDir = AppContext.BaseDirectory;
            string msiPath = Path.Combine(baseDir, "VoiceGardenSAPIAdapter.msi");

            if (!File.Exists(msiPath))
            {
                Notify($"MSI not found next to setup.exe:\n{msiPath}", Branding.SetupCaption, quiet, error: true);
                return 2;
            }

            string msiexecArgs;
            if (uninstall)
            {
                string? productCode = FindInstalledProductCode(Branding.ProductName);
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    Notify($"Could not find an installed {Branding.ProductName} product.", Branding.SetupCaption, quiet, error: true);
                    return 3;
                }

                // Uninstall removes the apps' files too — they must not run.
                if (!EnsureAppsClosed(quiet))
                    return 1602; // ERROR_INSTALL_USEREXIT

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
                // Upgrades replace the apps' files — a running instance keeps
                // them locked and the MSI fails or leaves a half-mixed install.
                if (!EnsureAppsClosed(quiet))
                    return 1602; // ERROR_INSTALL_USEREXIT

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
                        Notify("Uninstall completed.", Branding.SetupCaption, quiet, error: false);
                    }
                    else
                    {
                        if (!TryLaunchInstalledInstaller())
                        {
                            Notify("Install completed, but VoiceGarden.UI.exe was not found in the install location.", Branding.SetupCaption, quiet, error: true);
                            return 4;
                        }
                    }
                }
                else if (rc == 3010)
                {
                    Notify("Operation completed. A reboot is required.", Branding.SetupCaption, quiet, error: false);
                }
                else if (rc == 1602)
                {
                    Notify("Installation was cancelled.", Branding.SetupCaption, quiet, error: false);
                }
                else if (rc == 1625)
                {
                    Notify("Installation blocked by system policy (error 1625).\n\n" +
                           "This can happen if Windows Installer is restricted by your administrator.\n" +
                           "Try right-clicking setup.exe → 'Run as administrator'.\n\n" +
                           "On managed devices (e.g., Grid Pad), you may need to ask your " +
                           "administrator to allow MSI installations.", Branding.SetupCaption, quiet, error: true);
                }
                else
                {
                    Notify($"Setup failed with exit code {rc}.", Branding.SetupCaption, quiet, error: true);
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

    static bool HasArg(string[] args, string needle) =>
        args.Any(a => string.Equals(a, needle, StringComparison.OrdinalIgnoreCase));

    /// <summary>Apps whose files the MSI replaces — they must be closed first.</summary>
    static readonly string[] AppProcessNames = { "VoiceGarden.UI", "SherpaOnnxConfig" };

    /// <summary>
    /// Make sure none of the shipped apps are running. Asks the user first
    /// (with the option to close them automatically); silent runs close
    /// everything without asking. Returns false when the user declines.
    /// </summary>
    static bool EnsureAppsClosed(bool quiet)
    {
        if (!EnumerateAppProcesses().Any())
            return true; // nothing was running

        if (quiet)
        {
            CloseAppProcesses();
            return true;
        }

        var choice = MessageBox.Show(
            "VoiceGarden is currently running and must be closed before setup can continue.\n\n" +
            "Click OK to close it now and continue, or Cancel to stop setup.",
            Branding.SetupCaption,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);
        if (choice != DialogResult.OK)
            return false;

        CloseAppProcesses();

        // If something still refuses to die, say so rather than failing
        // the MSI with a cryptic file-in-use error.
        if (EnumerateAppProcesses().Any())
        {
            Notify("Could not close VoiceGarden (another user may be running it, or it may be busy). " +
                   "Close it manually and run setup again.",
                Branding.SetupCaption, quiet: false, error: true);
            return false;
        }
        return true;
    }

    static IEnumerable<Process> EnumerateAppProcesses() =>
        AppProcessNames.SelectMany(Process.GetProcessesByName).Distinct();

    static void CloseAppProcesses()
    {
        foreach (var proc in EnumerateAppProcesses().ToList())
        {
            try
            {
                // Ask nicely first (WM_CLOSE); the single-instance app exits on it.
                proc.CloseMainWindow();
                if (!proc.WaitForExit(3000))
                    proc.Kill();
            }
            catch
            {
                try { proc.Kill(); } catch { }
            }
        }
        // Give the OS a moment to release file handles.
        Thread.Sleep(1500);
    }

    static int RunMsiexec(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas", // Always elevate — MSI installs to ProgramFiles
        };

        try
        {
            using Process? p = Process.Start(psi);
            if (p == null)
                return 1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC
            return 1602; // ERROR_INSTALL_USEREXIT
        }
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

    static bool TryLaunchInstalledInstaller()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Branding.InstallFolderName, "VoiceGarden.UI.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Branding.InstallFolderName, "VoiceGarden.UI.exe"),
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
