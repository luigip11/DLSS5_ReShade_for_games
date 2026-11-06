using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private static Grid EqualActions(Button first, Button second)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
        first.HorizontalAlignment = second.HorizontalAlignment = HorizontalAlignment.Stretch;
        first.MinWidth = second.MinWidth = 0;
        first.Margin = new Thickness(0, 0, 6, 0); second.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(first); Grid.SetColumn(second, 1); row.Children.Add(second); return row;
    }
    private static void SmallAction(Button button, string label, string icon, bool primary = false)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var mark = DesignSystem.Icon(icon, primary ? BackgroundBrush : Accent, 17); mark.Margin = new Thickness(0, 0, 9, 0); row.Children.Add(mark);
        var text = Text(label, 12, primary ? BackgroundBrush : Ink, FontWeights.SemiBold); text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1); row.Children.Add(text); button.Content = row; button.MinHeight = 43;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }
    private static FrameworkElement LibraryFact(string icon, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(DesignSystem.Icon(icon, DesignSystem.Color("#8EAF95"), 15));
        var text = Text(value, 11, Muted); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = value;
        Grid.SetColumn(text, 1); row.Children.Add(text); return row;
    }
    private void AddSelectedGameCover(Grid hero, FrameworkElement text)
    {
        if (_report is null) return;
        var selected = _library.Games.FirstOrDefault(g => string.Equals(g.ExecutablePath, _report.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return;
        var original = ReadLibraryVariantGroups().FirstOrDefault(g => g.Variants.Any(v => v.Id == selected.Id))?.Original ?? selected;
        var image = CoverImage(original.Id);
        if (image is null) return;
        text.Margin = new Thickness(26, 24, 218, 26);
        var cover = new Border { Width = 155, Height = 191, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0), CornerRadius = new CornerRadius(10), BorderBrush = DesignSystem.Color("#587442"), BorderThickness = new Thickness(1),
            Background = Surface, Child = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(5) }, ClipToBounds = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(cover, "GameSelectedCover"); hero.Children.Add(cover);
    }
    private static void OpenProjectRepository()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/luigip11") { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(e.Message, "Profilo GitHub"); }
    }
}

/// <summary>Uses the available desktop width; card actions stay inside their equal-width card.</summary>
internal sealed class ResponsiveLibraryPanel : Panel
{
    private int _columns = 1;
    private double _width;
    protected override Size MeasureOverride(Size available)
    {
        _width = double.IsFinite(available.Width) ? Math.Max(1, available.Width) : Math.Max(313, ActualWidth);
        _columns = Math.Max(1, (int)((_width + 18) / 313));
        var cardWidth = Math.Max(1, (_width - (_columns - 1) * 18) / _columns);
        var y = 0d; var row = 0d; var index = 0;
        foreach (FrameworkElement child in InternalChildren)
        {
            var right = index % _columns == _columns - 1 ? 0 : 18;
            child.Width = cardWidth; child.Margin = new Thickness(0, 0, right, 18);
            child.Measure(new Size(cardWidth + right, double.PositiveInfinity)); row = Math.Max(row, child.DesiredSize.Height);
            if (++index % _columns == 0) { y += row; row = 0; }
        }
        return new Size(_width, y + row);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var y = 0d;
        for (var start = 0; start < InternalChildren.Count; start += _columns)
        {
            var height = 0d;
            for (var i = start; i < Math.Min(start + _columns, InternalChildren.Count); i++) height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
            var x = 0d;
            for (var i = start; i < Math.Min(start + _columns, InternalChildren.Count); i++)
            { var child = InternalChildren[i]; child.Arrange(new Rect(x, y, child.DesiredSize.Width, height)); x += child.DesiredSize.Width; }
            y += height;
        }
        return final;
    }
}
