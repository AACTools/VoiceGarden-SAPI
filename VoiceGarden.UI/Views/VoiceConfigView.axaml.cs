using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI.Views;

public partial class VoiceConfigView : UserControl
{
    public VoiceConfigView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}
