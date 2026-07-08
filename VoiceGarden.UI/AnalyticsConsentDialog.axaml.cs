using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI;

public class AnalyticsConsentDialog : Window
{
    public bool Result { get; private set; }

    public AnalyticsConsentDialog()
    {
        Title = "Help improve VoiceGarden";
        Width = 480;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        SystemDecorations = SystemDecorations.Full;
        ShowInTaskbar = false;

        var text = new TextBlock
        {
            Text = "VoiceGarden is a free, open-source project built by volunteers.\n\n" +
                   "To help us understand which TTS engines are most used, identify bugs, and " +
                   "prioritise development, we'd like to collect anonymous usage analytics.\n\n" +
                   "What we collect: engine types enabled, number of models downloaded, " +
                   "adapter registration.\n\n" +
                   "What we DON'T collect: spoken text, API keys, voice names, " +
                   "personal information, or audio content.\n\n" +
                   "You can change this anytime in Settings → Advanced.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(24, 24, 24, 12),
        };

        var link = new TextBlock
        {
            Text = "Read our full privacy policy",
            Margin = new Thickness(24, 0, 24, 16),
            Foreground = Avalonia.Media.Brushes.DodgerBlue,
            Classes = { "Hint" },
        };

        var yesBtn = new Button
        {
            Content = "Yes, help improve VoiceGarden",
            Padding = new Thickness(24, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "Primary" },
        };
        yesBtn.Click += (_, _) => { Result = true; Close(); };

        var noBtn = new Button
        {
            Content = "No thanks",
            Padding = new Thickness(24, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        noBtn.Click += (_, _) => { Result = false; Close(); };

        var btnPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Margin = new Thickness(24, 0, 24, 24),
            Children = { noBtn, yesBtn },
        };

        var panel = new StackPanel
        {
            Children = { text, link, btnPanel },
        };

        Content = panel;
    }
}
