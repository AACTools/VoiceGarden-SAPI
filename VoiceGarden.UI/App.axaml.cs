using Avalonia;
using Avalonia.Markup.Xaml;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI;

public partial class App : Application
{
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

            // First-run onboarding wizard (welcome + how-to + privacy)
            if (!AnalyticsService.PromptShown)
            {
                AnalyticsService.PromptShown = true;
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
}
