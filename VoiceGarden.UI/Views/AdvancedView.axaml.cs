using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI.Views;

public partial class AdvancedView : UserControl
{
    public AdvancedView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}
