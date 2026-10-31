using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly DisplayPreferences _displayPreferences = new(Path.Combine(GamePanelPaths.Root, "Settings"));
    private IReadOnlyList<DetectedDisplay> _displays = [];
    private string? _preferredDisplay;
    private string? _displayError;
    private sealed record DisplayChoice(string? Id, string Label);
    private DetectedDisplay? SelectedDisplay()
    {
        var saved = _displays.FirstOrDefault(d => d.Id == _preferredDisplay);
        var device = DisplayCatalog.WindowDevice(new WindowInteropHelper(this).Handle);
        return saved ?? _displays.FirstOrDefault(d => d.Device == device) ?? _displays.FirstOrDefault(d => d.Primary) ?? _displays.FirstOrDefault();
    }
    private void InitializeDisplays()
    {
        try { _preferredDisplay = _displayPreferences.Load().DisplayId; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { _displayError = e.Message; }
        RefreshDisplays();
    }
    private void RefreshDisplays()
    {
        try { _displays = DisplayCatalog.Read(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { _displays = []; _displayError = e.Message; }
        if (_page == 6 && IsLoaded) Render();
    }
    private void RenderDisplaySelector()
    {
        var label = Text("SCHERMO DI DESTINAZIONE", 10, Muted, FontWeights.SemiBold); label.Margin = new Thickness(0, 0, 0, 8); _content.Children.Add(label);
        var choices = new List<DisplayChoice> { new(null, "Automatico · schermo di GamePanel") };
        choices.AddRange(_displays.Select(d => new DisplayChoice(d.Id, d.Label)));
        var selected = choices.FindIndex(d => d.Id == _preferredDisplay);
        var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = nameof(DisplayChoice.Label), SelectedIndex = selected < 0 ? 0 : selected,
            IsEnabled = !_busy, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(combo, "TargetDisplay");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not DisplayChoice choice) return;
            try { _displayPreferences.Save(choice.Id); _preferredDisplay = choice.Id; _displayError = null; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { _displayError = e.Message; }
            Render();
        };
        _content.Children.Add(combo);
        var refresh = FxButton("Rileva schermi", "RefreshDisplays", (_, _) => RefreshDisplays(), !_busy); _content.Children.Add(refresh);
        if (_displayError is not null) Card("Preferenza schermo", _displayError);
        if (_preferredDisplay is not null && !_displays.Any(d => d.Id == _preferredDisplay)) Card("Schermo non collegato", "Lo schermo salvato non è disponibile. Per questa sessione uso quello di GamePanel; puoi scegliere un altro schermo.");
    }
}
