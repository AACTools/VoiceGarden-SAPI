using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI.Views;

public partial class VoicesView : UserControl
{
    public VoicesView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}
