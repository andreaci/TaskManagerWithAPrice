using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskCost;

public sealed class SortedColumnHeatConverter : IMultiValueConverter
{
    private static readonly Brush[] Palette = CreatePalette();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not double level ||
            values[1] is not string heatColumn || values[2] is not string cellColumn ||
            !string.Equals(heatColumn, cellColumn, StringComparison.Ordinal))
            return Brushes.Transparent;

        var index = (int)Math.Round(Math.Clamp(level, 0, 1) * (Palette.Length - 1));
        return Palette[index];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush[] CreatePalette()
    {
        var brushes = new Brush[101];
        var green = Color.FromRgb(198, 239, 206);
        var yellow = Color.FromRgb(255, 242, 178);
        var red = Color.FromRgb(255, 199, 206);
        for (var index = 0; index < brushes.Length; index++)
        {
            var position = index / 100d;
            var color = position <= .5
                ? Interpolate(green, yellow, position * 2)
                : Interpolate(yellow, red, (position - .5) * 2);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes[index] = brush;
        }
        return brushes;
    }

    private static Color Interpolate(Color start, Color end, double amount) => Color.FromRgb(
        (byte)Math.Round(start.R + (end.R - start.R) * amount),
        (byte)Math.Round(start.G + (end.G - start.G) * amount),
        (byte)Math.Round(start.B + (end.B - start.B) * amount));
}
