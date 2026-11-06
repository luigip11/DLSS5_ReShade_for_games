using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private static void DecorateAction(Button button, string label, string icon, bool primary = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mark = DesignSystem.Icon(icon, primary ? BackgroundBrush : Accent, primary ? 20 : 30);
        mark.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(mark);
        row.Children.Add(Text(label, primary ? 15 : 12, primary ? BackgroundBrush : Ink, FontWeights.SemiBold));
        button.Content = row; button.MinHeight = primary ? 52 : 66;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        if (primary)
        {
            button.Background = Accent; button.Foreground = BackgroundBrush; button.BorderBrush = Accent;
            button.Padding = new Thickness(22, 13, 22, 13); button.MinWidth = 230;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.MinWidth = 0;
        }
    }
    private void IllustratedHardwareCard(string title, string description, bool systemInformation = false)
    {
        var body = new StackPanel();
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition());
        var illustration = new Border { Child = DesignSystem.Icon("gpu", Accent, 45), Background = DesignSystem.Color("#1B3026"),
            BorderBrush = DesignSystem.Color("#3B5940"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 16, 0) };
        header.Children.Add(illustration);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(Text(title, 20, Ink, FontWeights.SemiBold));
        var caption = Text(systemInformation ? "IL SISTEMA SU CUI GIOCHI" : "ADATTATORI DEL SISTEMA", 9, Accent, FontWeights.SemiBold); caption.Margin = new Thickness(0, 6, 0, 0); text.Children.Add(caption);
        Grid.SetColumn(text, 1); header.Children.Add(text); body.Children.Add(header);
        if (systemInformation)
        {
            var specs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 22, 0, 0) };
            void Fact(string label, string value, string id)
            {
                var fact = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                fact.Children.Add(Text(label, 10, Muted, FontWeights.SemiBold));
                var highlight = Text(value, 15, Ink, FontWeights.SemiBold); highlight.Margin = new Thickness(0, 8, 0, 0);
                System.Windows.Automation.AutomationProperties.SetAutomationId(highlight, id); fact.Children.Add(highlight); specs.Children.Add(fact);
            }
            Fact("PROCESSORE", Hardware.Processor(), "HardwareCpu"); Fact("SCHEDA GRAFICA", description, "HardwareGpu"); Fact("MEMORIA RAM INSTALLATA", Hardware.Memory(), "HardwareRam");
            body.Children.Add(specs);
        }
        else { var details = Text(description, 12, Muted); details.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(details); }
        if (systemInformation)
        {
            var bottom = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            bottom.Children.Add(new Border { Height = 1, Background = DesignSystem.Line, Margin = new Thickness(0, 0, 0, 14) });
            var about = Button("Informazioni di sistema Windows", (_, _) => OpenWindowsSettings("ms-settings:about", "Apri Impostazioni → Sistema → Informazioni sul sistema."));
            SmallAction(about, "Informazioni di sistema Windows", "info"); about.HorizontalAlignment = HorizontalAlignment.Right; about.Margin = new Thickness(0);
            about.IsEnabled = !_busy; System.Windows.Automation.AutomationProperties.SetAutomationId(about, "WindowsSystemInfo"); bottom.Children.Add(about); body.Children.Add(bottom);
        }
        var panel = AssetPanel(body, 20);
        if (systemInformation) System.Windows.Automation.AutomationProperties.SetAutomationId(panel, "GameHardwareCard");
        _content.Children.Add(panel);
    }
    private void OpenWindowsSettings(string uri, string fallback)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, fallback + "\n" + e.Message, "Impostazioni Windows", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void RenderDlssHardware()
    {
        IllustratedHardwareCard("GPU rilevate in Windows", (_graphicsAdapters ??= Hardware.DisplayDevices())
            + "\n\nIl rilevamento elenca le schede disponibili e non misura quale sta usando il gioco. Gli identificativi della mod restano conservati.");
        var actions = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var screen = FxButton("Schermo e risoluzione", "DlssOpenScreen", (_, _) => OpenGraphicsPage(6), !_busy);
        DecorateAction(screen, "Schermo e risoluzione", "screen"); screen.Margin = new Thickness(0, 0, 7, 0); actions.Children.Add(screen);
        var gpu = FxButton("Preferenze GPU di Windows", "DlssGpuSettings", (_, _) => OpenWindowsSettings("ms-settings:display-advancedgraphics", "Apri Impostazioni → Sistema → Schermo → Grafica."), !_busy);
        DecorateAction(gpu, "Preferenze GPU di Windows", "gpu"); gpu.Margin = new Thickness(7, 0, 0, 0); Grid.SetColumn(gpu, 1); actions.Children.Add(gpu);
        _content.Children.Add(actions);
    }
}
