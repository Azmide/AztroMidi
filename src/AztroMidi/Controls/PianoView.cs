using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AztroMidi.Core;

namespace AztroMidi.Controls;

// Lightweight piano keyboard that lights up keys as they are pressed.
public sealed class PianoView : FrameworkElement
{
    private bool[] _active = new bool[128];
    private readonly bool[] _drawn = new bool[128];
    private int _low = KeyMapper.Low88;
    private int _high = KeyMapper.High88;
    private bool _showLabels = true;

    private static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF4)));
    private static readonly Brush Black = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1E, 0x23)));
    private static readonly Brush Lit = Freeze(new LinearGradientBrush(Color.FromRgb(0x4F, 0xC8, 0xF2), Color.FromRgb(0x0B, 0xA6, 0xDF), 90));
    private static readonly Brush LitBlack = Freeze(new LinearGradientBrush(Color.FromRgb(0x7C, 0xD3, 0xF4), Color.FromRgb(0x0A, 0x80, 0xAD), 90));
    private static readonly Brush Outside = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x75, 0x80)));
    private static readonly Brush LabelLit = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Pen Edge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1A)), 1));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public void SetSource(bool[] active) => _active = active;

    public void SetRange(KeyLayout layout)
    {
        (_low, _high) = KeyMapper.Range(layout);
        InvalidateVisual();
    }

    public bool ShowLabels
    {
        get => _showLabels;
        set { _showLabels = value; InvalidateVisual(); }
    }

    // Called on every frame; only repaints when a key changed.
    public void Tick()
    {
        bool dirty = false;
        for (int i = _low; i <= _high; i++)
        {
            if (_active[i] != _drawn[i]) { dirty = true; break; }
        }
        if (dirty) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        int whiteCount = 0;
        for (int p = _low; p <= _high; p++) if (!GeneralMidi.IsBlack(p)) whiteCount++;
        double kw = w / whiteCount;
        double bw = kw * 0.62, bh = h * 0.62;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface("Segoe UI");
        double fontSize = Math.Clamp(kw * 0.55, 7, 11);

        // White keys first.
        int x = 0;
        var whiteX = new Dictionary<int, double>();
        for (int p = _low; p <= _high; p++)
        {
            if (GeneralMidi.IsBlack(p)) continue;
            double left = x * kw;
            whiteX[p] = left;
            bool on = _active[p];
            _drawn[p] = on;
            var rect = new Rect(left + 0.5, 0, kw - 1, h);
            dc.DrawRoundedRectangle(on ? Lit : (p < KeyMapper.Low61 || p > KeyMapper.High61 ? Outside : White), null, rect, 3, 3);
            if (_showLabels && KeyMapper.Lookup(p) is KeyStroke k && kw >= 9)
            {
                var text = new FormattedText(k.Display, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    face, fontSize, on ? LabelLit : LabelBrush, dpi);
                dc.DrawText(text, new Point(left + (kw - text.Width) / 2, h - text.Height - 3));
            }
            x++;
        }

        // Black keys on top.
        for (int p = _low; p <= _high; p++)
        {
            if (!GeneralMidi.IsBlack(p) || !whiteX.TryGetValue(p - 1, out var prevLeft)) continue;
            bool on = _active[p];
            _drawn[p] = on;
            var rect = new Rect(prevLeft + kw - bw / 2, 0, bw, bh);
            dc.DrawRoundedRectangle(on ? LitBlack : Black, Edge, rect, 2, 2);
            if (_showLabels && KeyMapper.Lookup(p) is KeyStroke k && kw >= 11)
            {
                var text = new FormattedText(k.Display, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    face, fontSize * 0.85, on ? LabelLit : LabelBrush, dpi);
                dc.DrawText(text, new Point(rect.X + (bw - text.Width) / 2, bh - text.Height - 2));
            }
        }
    }
}
