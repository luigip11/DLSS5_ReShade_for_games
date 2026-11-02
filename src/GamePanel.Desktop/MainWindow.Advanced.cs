using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private static void StyleAdvanced(Expander expander)
    {
        var template = new ControlTemplate(typeof(Expander));
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(ToggleButton.IsCheckedProperty, new Binding("IsExpanded") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
        toggle.SetValue(ContentControl.ContentProperty, new Binding("Header") { RelativeSource = RelativeSource.TemplatedParent });
        toggle.SetValue(Control.ForegroundProperty, DesignSystem.Blue);
        toggle.SetValue(Control.FontSizeProperty, 13d); toggle.SetValue(Control.FontWeightProperty, FontWeights.SemiBold);
        toggle.SetValue(Control.BackgroundProperty, Surface);
        toggle.SetValue(Control.BorderBrushProperty, DesignSystem.Line);
        var buttonTemplate = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.PaddingProperty, new Thickness(12, 12, 12, 12));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var arrow = new FrameworkElementFactory(typeof(TextBlock), "Arrow"); arrow.SetValue(TextBlock.TextProperty, "›");
        arrow.SetValue(FrameworkElement.WidthProperty, 28d); arrow.SetValue(TextBlock.FontSizeProperty, 17d); arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(arrow); var header = new FrameworkElementFactory(typeof(ContentPresenter)); header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(header);
        border.AppendChild(row); buttonTemplate.VisualTree = border;
        var expanded = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        expanded.Setters.Add(new Setter(TextBlock.TextProperty, "⌄", "Arrow")); expanded.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); buttonTemplate.Triggers.Add(expanded);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); buttonTemplate.Triggers.Add(hover);
        toggle.SetValue(Control.TemplateProperty, buttonTemplate); stack.AppendChild(toggle);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 10, 0, 4));
        content.SetValue(UIElement.VisibilityProperty, new Binding("IsExpanded") { RelativeSource = RelativeSource.TemplatedParent, Converter = new BooleanToVisibilityConverter() });
        stack.AppendChild(content); template.VisualTree = stack; expander.Template = template;
    }
    private static void SpaceDlssPanel(Panel panel)
    {
        foreach (FrameworkElement child in panel.Children) child.Margin = new Thickness(0, 0, 0, 12);
    }
}
