using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI.Views;

public partial class CredentialsView : UserControl
{
    public CredentialsView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}
