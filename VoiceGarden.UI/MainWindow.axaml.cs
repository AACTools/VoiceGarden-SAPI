using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VoiceGarden.UI;

public partial class MainWindow : Window
{
    // Preferred size on large screens; clamped to the screen work-area on open.
    private const double DesiredWidth = 1040;
    private const double DesiredHeight = 780;

    public MainWindow()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);

        FlowDirection = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? (Avalonia.Media.FlowDirection)1
            : (Avalonia.Media.FlowDirection)0;

        // Set window icon from embedded ICO resource
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://VoiceGarden.UI/Assets/app.ico"));
            Icon = new WindowIcon(stream);
        }
        catch { /* don't crash if icon missing */ }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;

            // Work-area in DIPs (WorkingArea is physical pixels)
            var scale = screen.Scaling;
            var wa = screen.WorkingArea;
            var waWidthDip = wa.Width / scale;
            var waHeightDip = wa.Height / scale;

            // 90% of the work-area minus an allowance for the OS window frame
            // (measured ~16px width / ~40px height), never below the minimums,
            // never above the desired size
            Width = Math.Clamp(DesiredWidth, MinWidth, Math.Max(MinWidth, waWidthDip * 0.9 - 24));
            Height = Math.Clamp(DesiredHeight, MinHeight, Math.Max(MinHeight, waHeightDip * 0.9 - 48));

            // Re-center on the work-area (in physical pixels)
            var wPx = (int)(Width * scale);
            var hPx = (int)(Height * scale);
            Position = new PixelPoint(wa.X + (wa.Width - wPx) / 2, wa.Y + (wa.Height - hPx) / 2);
        }
        catch { /* never block launch on screen math */ }
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
