using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VoiceGarden.UI.ViewModels;

namespace VoiceGarden.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);

        // Set window icon (taskbar + title bar)
        try
        {
            var stream = AssetLoader.Open(new Uri("avares://VoiceGarden.UI/Assets/logo-small.png"));
            Icon = new WindowIcon(stream);
        }
        catch { }
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
