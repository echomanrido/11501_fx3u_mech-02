using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Fx3uMonitorApp.Controls;

/// <summary>
/// 高質感工業級立體圓形 LED 指示燈元件
/// </summary>
public class LedBulb : Control
{
    private Color _onColor = Color.FromArgb(0, 230, 118); // 亮綠色
    private Color _offColor = Color.FromArgb(55, 71, 79);  // 暗灰黑
    private bool _isOn = false;

    public LedBulb()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(24, 24);
    }

    [Category("LED 屬性")]
    [Description("指示燈當前狀態 (ON / OFF)")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn != value)
            {
                _isOn = value;
                Invalidate();
            }
        }
    }

    [Category("LED 屬性")]
    [Description("亮燈時的發光顏色")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color OnColor
    {
        get => _onColor;
        set
        {
            _onColor = value;
            Invalidate();
        }
    }

    [Category("LED 屬性")]
    [Description("滅燈時的底色")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color OffColor
    {
        get => _offColor;
        set
        {
            _offColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int diameter = Math.Min(Width, Height) - 4;
        if (diameter <= 0) return;

        Rectangle rect = new((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);

        // 1. 繪製外圈金屬邊框 (Bezel)
        using (var borderPen = new Pen(Color.FromArgb(90, 100, 110), 1.5f))
        {
            e.Graphics.DrawEllipse(borderPen, rect);
        }

        // 2. 繪製主體圓形（漸層質感）
        Rectangle innerRect = rect;
        innerRect.Inflate(-1, -1);

        Color centerColor = _isOn ? ControlPaint.LightLight(_onColor) : ControlPaint.Light(_offColor);
        Color edgeColor = _isOn ? _onColor : _offColor;

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(innerRect);
            using (var pthGrBrush = new PathGradientBrush(path))
            {
                pthGrBrush.CenterColor = centerColor;
                pthGrBrush.SurroundColors = [edgeColor];
                e.Graphics.FillEllipse(pthGrBrush, innerRect);
            }
        }

        // 3. 若點亮，繪製高光反光點 (Gloss highlight)
        if (_isOn)
        {
            Rectangle highlightRect = new(
                innerRect.X + innerRect.Width / 4,
                innerRect.Y + innerRect.Height / 8,
                innerRect.Width / 2,
                innerRect.Height / 3);

            using (var highlightBrush = new LinearGradientBrush(
                highlightRect,
                Color.FromArgb(200, Color.White),
                Color.FromArgb(0, Color.White),
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillEllipse(highlightBrush, highlightRect);
            }
        }
    }
}
