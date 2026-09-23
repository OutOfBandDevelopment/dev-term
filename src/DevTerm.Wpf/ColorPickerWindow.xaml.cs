using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DevTerm.Wpf;

/// <summary>
/// A modal RGB/HSV color picker, opened by either front-end renderer's generic
/// <c>ButtonControl.ColorPickerTargetCommandId</c> support (see <see cref="ControlPanelWindow"/>) —
/// not specific to any one device. All three representations (RGB sliders, HSV sliders, and a hex
/// text field) stay in sync via <see cref="_updating"/>, which suppresses the re-entrant update each
/// control's own change handler would otherwise trigger on the other two.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private bool _updating;

    public byte SelectedR { get; private set; }

    public byte SelectedG { get; private set; }

    public byte SelectedB { get; private set; }

    public ColorPickerWindow(byte initialR, byte initialG, byte initialB)
    {
        InitializeComponent();
        SelectedR = initialR;
        SelectedG = initialG;
        SelectedB = initialB;
        SetFromRgb(initialR, initialG, initialB);
    }

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating)
        {
            return;
        }

        SetFromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
    }

    private void HsvSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating)
        {
            return;
        }

        var (r, g, b) = HsvToRgb(HSlider.Value, SSlider.Value / 100.0, VSlider.Value / 100.0);
        SetFromRgb(r, g, b);
    }

    private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
        }
    }

    private void HexTextBox_LostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (_updating)
        {
            return;
        }

        if (TryParseHex(HexTextBox.Text, out var r, out var g, out var b))
        {
            SetFromRgb(r, g, b);
        }
        else
        {
            HexTextBox.Text = FormatHex(SelectedR, SelectedG, SelectedB);
        }
    }

    private void SetFromRgb(byte r, byte g, byte b)
    {
        _updating = true;
        try
        {
            SelectedR = r;
            SelectedG = g;
            SelectedB = b;

            RSlider.Value = r;
            GSlider.Value = g;
            BSlider.Value = b;
            RValueText.Text = r.ToString(CultureInfo.InvariantCulture);
            GValueText.Text = g.ToString(CultureInfo.InvariantCulture);
            BValueText.Text = b.ToString(CultureInfo.InvariantCulture);

            var (h, s, v) = RgbToHsv(r, g, b);
            HSlider.Value = h;
            SSlider.Value = s * 100.0;
            VSlider.Value = v * 100.0;
            HValueText.Text = h.ToString("0", CultureInfo.InvariantCulture);
            SValueText.Text = (s * 100.0).ToString("0", CultureInfo.InvariantCulture);
            VValueText.Text = (v * 100.0).ToString("0", CultureInfo.InvariantCulture);

            HexTextBox.Text = FormatHex(r, g, b);
            PreviewSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        finally
        {
            _updating = false;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private static string FormatHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    private static bool TryParseHex(string text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var trimmed = text.Trim().TrimStart('#');
        if (trimmed.Length != 6)
        {
            return false;
        }

        return byte.TryParse(trimmed.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(trimmed.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(trimmed.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    /// <summary>Standard RGB→HSV conversion; hue in degrees [0,360), saturation/value in [0,1].</summary>
    internal static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        double hue;
        if (delta < 1e-9)
        {
            hue = 0;
        }
        else if (max == rf)
        {
            hue = 60 * (((gf - bf) / delta) % 6);
        }
        else if (max == gf)
        {
            hue = 60 * (((bf - rf) / delta) + 2);
        }
        else
        {
            hue = 60 * (((rf - gf) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max < 1e-9 ? 0 : delta / max;
        var value = max;
        return (hue, saturation, value);
    }

    /// <summary>Standard HSV→RGB conversion; hue in degrees [0,360), saturation/value in [0,1].</summary>
    internal static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var hPrime = (h % 360) / 60.0;
        var x = c * (1 - Math.Abs((hPrime % 2) - 1));
        var (r1, g1, b1) = hPrime switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        var m = v - c;
        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }
}
