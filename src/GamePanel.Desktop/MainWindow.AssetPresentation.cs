using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    // Existing renderers append to the page; move their output into a tab without
    // duplicating controls or changing their automation identifiers.
    private StackPanel CaptureAssetContent(Action render)
    {
        var first = _content.Children.Count;
        render();
        var panel = new StackPanel();
        while (_content.Children.Count > first)
        {
            var child = _content.Children[first];
            _content.Children.RemoveAt(first);
            panel.Children.Add(child);
        }
        return panel;
    }

    private TabControl CreateAssetTabs(StackPanel available, StackPanel installed)
    {
        var tabs = new TabControl
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Ink, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(tabs, "AssetsTabs");
        var layout = new FrameworkElementFactory(typeof(DockPanel));
        var headers = new FrameworkElementFactory(typeof(TabPanel));
        headers.SetValue(Panel.IsItemsHostProperty, true);
        headers.SetValue(DockPanel.DockProperty, Dock.Top);
        headers.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 18));
        layout.AppendChild(headers);
        var content = new FrameworkElementFactory(typeof(ContentPresenter), "PART_SelectedContentHost");
        content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        layout.AppendChild(content);
        tabs.Template = new ControlTemplate(typeof(TabControl)) { VisualTree = layout };

        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, DesignSystem.Line));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(20, 13, 20, 13));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        label.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(label);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(TabItem)) { VisualTree = border }));
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        selected.Setters.Add(new Setter(Control.BackgroundProperty, DesignSystem.Color("#203026")));
        style.Triggers.Add(selected);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Control.BorderBrushProperty, DesignSystem.Blue));
        style.Triggers.Add(focused);
        tabs.ItemContainerStyle = style;

        var availableTab = new TabItem { Header = "Da installare", Content = available };
        var installedTab = new TabItem { Header = "I tuoi pacchetti", Content = installed };
        System.Windows.Automation.AutomationProperties.SetAutomationId(availableTab, "AssetsAvailableTab");
        System.Windows.Automation.AutomationProperties.SetAutomationId(installedTab, "AssetsInstalledTab");
        tabs.Items.Add(availableTab);
        tabs.Items.Add(installedTab);
        tabs.SelectedIndex = _assetsTab;
        tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, tabs) || tabs.SelectedIndex == _assetsTab) return;
            _assetsTab = tabs.SelectedIndex;
            if (_assetPreviewPanel is { Parent: Panel parent } preview)
            {
                parent.Children.Remove(preview);
                if (_assetsTab == 1) installed.Children.Insert(0, preview);
                else available.Children.Add(preview);
            }
            _pageScroll.ScrollToTop();
        };
        return tabs;
    }

    private enum AssetFeedback { None, Success, Attention }

    private static Border AssetFeedbackCard(AssetFeedback feedback, string title, TextBlock status)
    {
        var success = feedback == AssetFeedback.Success;
        var color = success ? DesignSystem.Color("#79DF9E") : DesignSystem.Amber;
        var body = new StackPanel();
        body.Children.Add(Text((success ? "✓  " : "!  ") + title, 18, color, FontWeights.Bold));
        status.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(status);
        return new Border
        {
            Background = DesignSystem.Color(success ? "#142C23" : "#30271C"),
            BorderBrush = color, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16), Margin = new Thickness(0, 14, 0, 14), Child = body
        };
    }
}
