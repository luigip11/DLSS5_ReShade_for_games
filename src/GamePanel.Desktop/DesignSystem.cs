using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GamePanel.Desktop;

internal static class DesignSystem
{
    public static readonly Brush Background = Color("#0A0F16");
    public static readonly Brush Sidebar = Color("#0D141F");
    public static readonly Brush Panel = Color("#121D2B");
    public static readonly Brush Raised = Color("#19283A");
    public static readonly Brush Line = Color("#27384D");
    public static readonly Brush Text = Color("#F0F5FC");
    public static readonly Brush Muted = Color("#9FAFC3");
    public static readonly Brush Lime = Color("#BCFF69");
    public static readonly Brush Blue = Color("#6BA9FF");
    public static readonly Brush Amber = Color("#FFC779");
    public static SolidColorBrush Color(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze(); return brush;
    }
    public static TextBlock Label(string text, double size = 13, Brush? color = null, bool strong = false) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap,
        FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal
    };
    public static Border Chip(string text, Brush? foreground = null, Brush? background = null) => new()
    {
        CornerRadius = new CornerRadius(6), Background = background ?? Raised,
        Padding = new Thickness(9, 5, 9, 5), Child = Label(text, 10, foreground ?? Muted, true),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
    };
    public static FrameworkElement Icon(string kind, Brush? stroke = null, double size = 20)
    {
        var data = kind switch
        {
            "game" => "M5,7 L19,7 22,16 Q23,21 18,19 L15,16 9,16 6,19 Q1,21 2,16 Z M6,10 L6,14 M4,12 L8,12 M16,11 L16,11.2 M19,13 L19,13.2",
            "chip" => "M6,6 L18,6 18,18 6,18 Z M9,9 L15,9 15,15 9,15 Z M9,2 L9,6 M15,2 L15,6 M9,18 L9,22 M15,18 L15,22 M2,9 L6,9 M2,15 L6,15 M18,9 L22,9 M18,15 L22,15",
            "sun" => "M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 M12,1 L12,4 M12,20 L12,23 M1,12 L4,12 M20,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
            "shield" => "M12,2 L21,6 20,14 Q18,20 12,23 Q6,20 4,14 L3,6 Z M8,12 L11,15 16,9",
            "scan" => "M8,3 L3,3 3,8 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16 M7,12 L17,12 M12,7 L12,17",
            "folder" => "M2,6 L9,6 11,8 22,8 22,20 2,20 Z M2,6 L2,4 9,4 11,6 20,6 20,8",
            "arrow" => "M4,12 L20,12 M14,6 L20,12 14,18",
            "download" => "M12,2 L12,16 M6,10 L12,16 18,10 M3,16 L3,22 21,22 21,16",
            _ => "M4,4 L20,4 20,20 4,20 Z"
        };
        var geometry = Geometry.Parse(data); geometry.Freeze();
        return new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Child = new Path { Data = geometry, Stroke = stroke ?? Muted, StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Width = 24, Height = 24 }
        };
    }
    public static Style ButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Raised));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True" />
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.84" /></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.65" /></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#BCFF69" /></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.4" /></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        return style;
    }
    public static FrameworkElement HeroArtwork()
    {
        var canvas = new Canvas { Width = 620, Height = 300, IsHitTestVisible = false };
        for (var i = 0; i < 12; i++)
            canvas.Children.Add(new Line { X1 = 210 + i * 37, Y1 = -10, X2 = 45 + i * 37, Y2 = 310,
                Stroke = Color("#243B58"), StrokeThickness = 1, Opacity = .55 });
        var lines = new[]
        {
            ("M280,0 L420,0 560,150 420,300 280,300 420,150Z", "#24456B", 1.3),
            ("M365,24 L493,150 365,276", "#6BA9FF", 2.2),
            ("M404,63 L490,150 404,237", "#BCFF69", 3.0),
            ("M447,102 L494,150 447,198", "#BCFF69", 6.0)
        };
        foreach (var (path, hex, width) in lines)
            canvas.Children.Add(new Path { Data = Geometry.Parse(path), Stroke = Color(hex), StrokeThickness = width,
                StrokeLineJoin = PenLineJoin.Round, Opacity = .8 });
        return new Viewbox { Child = canvas, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Right, Opacity = .8, IsHitTestVisible = false };
    }
    public static Style ScrollBarStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Setter Property="Width" Value="10" /><Setter Property="Background" Value="Transparent" />
          <Setter Property="Focusable" Value="False" />
          <Setter Property="Template"><Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Grid Background="Transparent" Margin="2">
                <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}"
                       Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" ViewportSize="{TemplateBinding ViewportSize}"
                       Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <Track.DecreaseRepeatButton><RepeatButton x:Name="Decrease" Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0" Focusable="False" /></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb x:Name="Grip" MinHeight="28" Background="#354A65">
                    <Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="3" /></ControlTemplate></Thumb.Template>
                  </Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton x:Name="Increase" Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0" Focusable="False" /></Track.IncreaseRepeatButton>
                </Track>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger SourceName="Grip" Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="#6BA9FF" /></Trigger>
                <Trigger Property="Orientation" Value="Horizontal">
                  <Setter Property="Width" Value="Auto" /><Setter Property="Height" Value="10" />
                  <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False" />
                  <Setter TargetName="Grip" Property="MinHeight" Value="0" /><Setter TargetName="Grip" Property="MinWidth" Value="28" />
                  <Setter TargetName="Decrease" Property="Command" Value="{x:Static ScrollBar.PageLeftCommand}" />
                  <Setter TargetName="Increase" Property="Command" Value="{x:Static ScrollBar.PageRightCommand}" />
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value></Setter>
        </Style>
        """);
    public static Style ComboBoxStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Setter Property="Foreground" Value="#F0F5FC" /><Setter Property="Background" Value="#19283A" />
          <Setter Property="BorderBrush" Value="#354A65" /><Setter Property="MinHeight" Value="46" />
          <Setter Property="ItemContainerStyle"><Setter.Value>
            <Style TargetType="ComboBoxItem">
              <Setter Property="Foreground" Value="#F0F5FC" /><Setter Property="Padding" Value="12,10" />
              <Setter Property="HorizontalContentAlignment" Value="Stretch" />
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
                <Border x:Name="Item" CornerRadius="5" Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter /></Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Item" Property="Background" Value="#273D57" /></Trigger>
                  <Trigger Property="IsSelected" Value="True"><Setter TargetName="Item" Property="Background" Value="#263C2B" /><Setter Property="Foreground" Value="#BCFF69" /></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
          </Setter.Value></Setter>
          <Setter Property="Template"><Setter.Value>
            <ControlTemplate TargetType="ComboBox">
              <Grid>
                <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" ClickMode="Press">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                    <Border Background="#19283A" BorderBrush="#354A65" BorderThickness="1" CornerRadius="9">
                      <Path Data="M0,0 L5,5 10,0" Stroke="#9FAFC3" StrokeThickness="1.5" Width="10" Height="5" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,16,0" />
                    </Border>
                  </ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                  Margin="14,0,38,0" HorizontalAlignment="Left" VerticalAlignment="Center" />
                <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False">
                  <Border Background="#19283A" BorderBrush="#354A65" BorderThickness="1" CornerRadius="9" Padding="4" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" Margin="0,5,0,0">
                    <ScrollViewer MaxHeight="260"><ItemsPresenter /></ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45" /></Trigger></ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value></Setter>
        </Style>
        """);
    public static ImageSource AppIcon()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Background, null, new RectangleGeometry(new Rect(0, 0, 64, 64), 12, 12)));
        drawing.Children.Add(new GeometryDrawing(Lime, null, Geometry.Parse("M12,15 L23,15 40,32 23,49 12,49 29,32Z")));
        drawing.Children.Add(new GeometryDrawing(Blue, null, Geometry.Parse("M31,15 L42,15 59,32 42,49 31,49 48,32Z")));
        drawing.Freeze(); var icon = new DrawingImage(drawing); icon.Freeze(); return icon;
    }
}
