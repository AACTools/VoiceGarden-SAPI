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

            // First-run analytics consent dialog
            if (!AnalyticsService.PromptShown)
            {
                AnalyticsService.PromptShown = true;
                var dialog = new AnalyticsConsentDialog();
                dialog.ShowDialog(mainWindow);
                dialog.Closed += (_, _) =>
                {
                    if (dialog.Result)
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
