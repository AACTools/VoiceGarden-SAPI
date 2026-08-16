using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI.Views;

public partial class EnginesView : UserControl
{
    public EnginesView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}
