using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            bool uninstall = HasArg(args, "--uninstall") || HasArg(args, "/uninstall");
            bool quiet = HasArg(args, "--silent") || HasArg(args, "/silent") || HasArg(args, "/quiet");
            bool removeAppData = HasArg(args, "--remove-appdata");

            string baseDir = AppContext.BaseDirectory;
            string msiPath = Path.Combine(baseDir, "NaturalVoiceSAPIAdapter.msi");

            if (!File.Exists(msiPath))
            {
                Notify($"MSI not found next to setup.exe:\n{msiPath}", "NaturalVoiceSAPIAdapter Setup", quiet, error: true);
                return 2;
            }

            string msiexecArgs;
            if (uninstall)
            {
                string? productCode = FindInstalledProductCode("NaturalVoiceSAPIAdapter");
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    Notify("Could not find an installed NaturalVoiceSAPIAdapter product.", "NaturalVoiceSAPIAdapter Setup", quiet, error: true);
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
                        Notify("Uninstall completed.", "NaturalVoiceSAPIAdapter Setup", quiet, error: false);
                    }
                    else
                    {
                        if (!TryLaunchInstalledInstaller())
                        {
                            Notify("Install completed, but Installer.exe was not found in the install location.", "NaturalVoiceSAPIAdapter Setup", quiet, error: true);
                            return 4;
                        }
                    }
                }
                else if (rc == 3010)
                {
                    Notify("Operation completed. A reboot is required.", "NaturalVoiceSAPIAdapter Setup", quiet, error: false);
                }
                else if (rc == 1618)
                {
                    Notify("Another installer is currently running. Please wait and try again.", "NaturalVoiceSAPIAdapter Setup", quiet, error: true);
                }
                else
                {
                    Notify($"Setup failed with exit code {rc}.", "NaturalVoiceSAPIAdapter Setup", quiet, error: true);
                }
            }
            return rc;
        }
        catch (Exception ex)
        {
            Notify(ex.Message, "NaturalVoiceSAPIAdapter Setup", quiet: false, error: true);
            return 1;
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

    static bool TryLaunchInstalledInstaller()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NaturalVoiceSAPIAdapter", "Installer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NaturalVoiceSAPIAdapter", "Installer.exe")
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
