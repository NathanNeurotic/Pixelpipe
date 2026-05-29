using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pixelpipe
{
    // GUI-6 (v0.14.0): small colored dot rendered into a Control so it can
    // sit inline next to status text. Cheap to instantiate (one Control
    // each) and the OnPaint is two GDI+ calls; lifetime matches the parent
    // form so we don't worry about disposing here.
    //
    // The audit's "ok / warn / err / unknown" colour family is centralised
    // here rather than at each call site so all dots stay consistent.
    internal sealed class StatusDot : Control
    {
        public enum DotColor { Unknown, Ok, Warn, Error }

        private DotColor _state = DotColor.Unknown;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            // 10 px is small enough to look chip-like next to a label.
            Width = 10;
            Height = 10;
            Margin = new Padding(6, 8, 6, 0);
            TabStop = false;
        }

        public DotColor State
        {
            get { return _state; }
            set { if (_state != value) { _state = value; Invalidate(); } }
        }

        // Convenience setter: pass `true` for ok, `false` for error. Use
        // SetState(DotColor) for the warn/unknown cases.
        public void SetOk(bool ok) { State = ok ? DotColor.Ok : DotColor.Error; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Color fill;
            switch (_state)
            {
                case DotColor.Ok:    fill = StatusDotColors.Ok;    break;
                case DotColor.Warn:  fill = StatusDotColors.Warn;  break;
                case DotColor.Error: fill = StatusDotColors.Error; break;
                default:             fill = StatusDotColors.Unknown; break;
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int diameter = System.Math.Min(Width, Height) - 1;
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillEllipse(brush, 0, 0, diameter, diameter);
            }
        }
    }

    internal static class StatusDotColors
    {
        // Slightly more saturated than WindowTheme's status colors so the
        // 10×10 fill reads as a dot at small sizes rather than a smudge.
        public static readonly Color Ok      = Color.FromArgb(80, 200, 110);
        public static readonly Color Warn    = Color.FromArgb(240, 180, 60);
        public static readonly Color Error   = Color.FromArgb(240, 100, 100);
        public static readonly Color Unknown = Color.FromArgb(120, 130, 145);
    }

    // GUI-2 (v0.14.0): owner-drawn dark-themed progress bar. Replaces the
    // OS-styled `ProgressBar` whose green chunk on a light trough fought
    // the rest of the dark UI. Paints itself in WindowTheme colors at any
    // size; honours Dock/Anchor so it reflows with the card.
    internal sealed class ThemedBar : Control
    {
        private int _value;
        public ThemedBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 6;
            TabStop = false;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
                if (_value != clamped) { _value = clamped; Invalidate(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (SolidBrush trough = new SolidBrush(WindowTheme.InputBg))
            {
                e.Graphics.FillRectangle(trough, 0, 0, Width, Height);
            }
            int fillWidth = (Width * _value) / 100;
            if (fillWidth <= 0) return;
            using (SolidBrush fill = new SolidBrush(WindowTheme.AccentColor))
            {
                e.Graphics.FillRectangle(fill, 0, 0, fillWidth, Height);
            }
        }
    }
}
