using Avalonia;
using Avalonia.Markup.Xaml;
using Microsoft.Win32;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI;

public partial class App : Application
{
    private const string RegPath = @"SOFTWARE\VoiceGardenSAPIAdapter";

    /// <summary>
    /// Bump this when onboarding content changes to force all users to see it again.
    /// Also used to detect fresh installs (stored version = 0).
    /// v2: walkthrough rewritten for the 3-tab Engines/Credentials/Voices flow.
    /// </summary>
    private const int CurrentOnboardingVersion = 2;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = new ViewModels.MainViewModel(),
            };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();

            if (ShouldShowOnboarding())
            {
                MarkOnboardingShown();
                var onboarding = new OnboardingWindow();
                onboarding.ShowDialog(mainWindow);
                onboarding.Closed += (_, _) =>
                {
                    if (onboarding.AnalyticsOptIn)
                    {
                        AnalyticsService.IsEnabled = true;
                        AnalyticsService.Track("analytics_opted_in");
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Show onboarding if:
    /// - Never seen before (OnboardingVersion missing = 0), OR
    /// - Onboarding version was bumped (stored &lt; current), OR
    /// - Analytics consent was never answered (PromptShown missing)
    /// </summary>
    private static bool ShouldShowOnboarding()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            if (key == null) return true;

            var storedVersion = (int)(key.GetValue("OnboardingVersion") ?? 0);
            var promptShown = key.GetValue("AnalyticsPromptShown");

            return storedVersion < CurrentOnboardingVersion || promptShown == null;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Record that onboarding was shown. AnalyticsId is preserved so PostHog
    /// tracks the same machine across reinstalls/upgrades.
    /// </summary>
    private static void MarkOnboardingShown()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath, writable: true);
            key?.SetValue("OnboardingVersion", CurrentOnboardingVersion, RegistryValueKind.DWord);
            key?.SetValue("AnalyticsPromptShown", 1, RegistryValueKind.DWord);
        }
        catch { }
    }
}
