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

        // Set window icon from embedded ICO resource
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://VoiceGarden.UI/Assets/app.ico"));
            Icon = new WindowIcon(stream);
        }
        catch { /* don't crash if icon missing */ }
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
