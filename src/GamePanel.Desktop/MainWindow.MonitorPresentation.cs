using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private StackPanel? _screenResolutionHost;
    private TextBlock? _monitorResolutionText;

    private void UpdateMonitorResolution(int width, int height)
    { if (_monitorResolutionText is not null) _monitorResolutionText.Text = $"{width} × {height}"; }

    private void RenderScreenMonitor(DetectedDisplay? display)
    {
        var body = new StackPanel(); var header = new Grid();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = DesignSystem.Icon("screen", Accent, 23); icon.Margin = new Thickness(0, 0, 12, 0); title.Children.Add(icon);
        title.Children.Add(Text("Il tuo schermo", 22, Ink, FontWeights.SemiBold)); header.Children.Add(title);
        var chip = DesignSystem.Chip("MONITOR SELEZIONATO", Accent); chip.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(chip); body.Children.Add(header);
        var drawing = new StackPanel { Width = 360 };
        var face = new Grid { Height = display is null ? 185 : 342d * display.TargetHeight / Math.Max(1, display.TargetWidth), Background = DesignSystem.Color("#0A171C") };
        _monitorResolutionText = Text(display is null ? "Da rilevare" : $"{display.TargetWidth} × {display.TargetHeight}", 29, Accent, FontWeights.SemiBold);
        _monitorResolutionText.HorizontalAlignment = HorizontalAlignment.Center; _monitorResolutionText.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_monitorResolutionText, "DetectedDisplayResolution"); face.Children.Add(_monitorResolutionText);
        drawing.Children.Add(new Border { Child = face, Padding = new Thickness(7), CornerRadius = new CornerRadius(13), BorderBrush = DesignSystem.Color("#729350"), BorderThickness = new Thickness(2), Background = DesignSystem.Raised });
        drawing.Children.Add(new Border { Width = 36, Height = 23, Background = DesignSystem.Color("#415345") });
        drawing.Children.Add(new Border { Width = 125, Height = 7, Background = DesignSystem.Color("#729350"), CornerRadius = new CornerRadius(4) });
        var picture = new Viewbox { Child = drawing, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Height = 225, Margin = new Thickness(0, 22, 0, 22) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(picture, "SelectedMonitorPreview"); body.Children.Add(picture);
        var selectors = new Grid(); selectors.ColumnDefinitions.Add(new ColumnDefinition()); selectors.ColumnDefinitions.Add(new ColumnDefinition());
        var monitor = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; monitor.Children.Add(Text("MONITOR", 10, Muted, FontWeights.SemiBold));
        var monitorChoice = CreateDisplaySelector(); monitorChoice.Margin = new Thickness(0, 8, 0, 0); monitor.Children.Add(monitorChoice); selectors.Children.Add(monitor);
        _screenResolutionHost = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(_screenResolutionHost, 1); selectors.Children.Add(_screenResolutionHost);
        _screenResolutionHost.Children.Add(Text("RISOLUZIONE DEL GIOCO", 10, Muted, FontWeights.SemiBold));
        if (display is not null)
        {
            var modes = DisplayCatalog.Modes(display);
            var mode = new ComboBox { ItemsSource = modes, SelectedItem = modes.FirstOrDefault(m => m.Width == display.TargetWidth && m.Height == display.TargetHeight) ?? modes[0], IsEnabled = !_busy, Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(mode, "ScreenResolutionPreviewChoice");
            mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is DisplayCatalog.ResolutionSize value) UpdateMonitorResolution(value.Width, value.Height); };
            _screenResolutionHost.Children.Add(mode);
        }
        else _screenResolutionHost.Children.Add(Text("Nessuno schermo rilevato", 12, Muted));
        body.Children.Add(selectors);
        if (display is not null) { var caption = Text(display.Name + $" · Windows {display.Width} × {display.Height}", 11, Muted); caption.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(caption); }
        var refresh = FxButton("Rileva schermi", "RefreshDisplays", (_, _) => RefreshDisplays(), !_busy); SmallAction(refresh, "Rileva schermi", "refresh");
        refresh.HorizontalAlignment = HorizontalAlignment.Right; refresh.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(refresh); AddDisplayMessages(body);
        _content.Children.Add(AssetPanel(body, 22));
    }
}
