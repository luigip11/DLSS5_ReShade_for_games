using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GamePanel.Desktop;
internal sealed partial class MainWindow
{
    // Small explanatory diagrams, never a preview of a game's actual rendered output.
    private static void DrawDlssSketch(Canvas canvas, string kind, int? value)
    {
        canvas.Children.Clear(); canvas.ToolTip = "Schema illustrativo del parametro, non un'anteprima del gioco o una misura delle prestazioni.";
        void Line(double x, double y, double a, double b, double opacity = 1)
            => canvas.Children.Add(new Line { X1 = x, Y1 = y, X2 = a, Y2 = b, Stroke = Accent, StrokeThickness = 1.5, Opacity = opacity });
        void Box(double x, double y, double w, double h, double opacity = 1)
        {
            var box = new Rectangle { Width = w, Height = h, RadiusX = 3, RadiusY = 3, Stroke = Accent, StrokeThickness = 1.2, Opacity = opacity };
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y); canvas.Children.Add(box);
        }
        if (kind == "DlssQuality")
        {
            for (var y = 0; y < 3; y++) for (var x = 0; x < 3; x++) Box(3 + x * 6, 17 + y * 6, 3, 3, .45);
            Line(25, 26, 36, 26); Line(32, 22, 36, 26); Line(32, 30, 36, 26);
            Box(42, 11, 30, 30); Line(46, 35, 54, 24); Line(54, 24, 61, 31); Line(61, 31, 69, 17);
        }
        else if (kind == "DlssFrameGeneration")
        {
            var count = value is > 0 ? value.Value + 1 : 2;
            for (var i = 0; i < count; i++) Box(4 + i * (62d / count), 13 + (i % 2) * 5, 15, 24, i == 0 ? 1 : .35 + i * .12);
            Line(5, 46, 70, 46, .4);
        }
        else if (kind == "DlssReflex")
        {
            Line(3, 27, 20, 27, .45); Line(20, 27, 30, 12); Line(30, 12, 39, 40); Line(39, 40, 49, 20); Line(49, 20, 57, 27); Line(57, 27, 73, 27);
        }
        else if (kind == "DlssReconstructionModel")
        {
            for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) Line(10, 12 + i * 14, 38, 12 + j * 14, .3);
            for (var i = 0; i < 3; i++) { Box(7, 9 + i * 14, 6, 6); Box(35, 9 + i * 14, 6, 6); Line(41, 12 + i * 14, 66, 26, .5); }
            Box(63, 23, 6, 6);
        }
        else
        {
            for (var i = 0; i < 12; i++) Box(4 + i % 4 * 6, 10 + i / 4 * 12, 2, 2, .35);
            Line(32, 8, 32, 45, .4); Line(40, 39, 52, 17); Line(52, 17, 70, 34);
        }
        canvas.Opacity = value is null ? .45 : 1;
    }
}
