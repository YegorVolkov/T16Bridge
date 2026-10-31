using System.Numerics;
using System.Drawing.Drawing2D;

namespace T16Bridge;

public readonly record struct StickButtonPosition(int Number, int X, int Y);
public readonly record struct StickButtonShape(int Number, PointF[] Outline);

public static class T16000Layout
{
    // Top view, player at the bottom. Thrustmaster user manual, diagrams 3 and 4:
    // https://ts.thrustmaster.com/download/accessories/Manuals/T16000M/T16000M-User_manual.pdf
    public static IReadOnlyList<StickButtonPosition> Buttons(bool leftHanded)
        => Shapes(leftHanded).Select(shape => new StickButtonPosition(shape.Number,
            (int)shape.Outline.Min(p => p.X), (int)shape.Outline.Min(p => p.Y))).ToArray();

    public static IReadOnlyList<StickButtonShape> Shapes(bool leftHanded)
    {
        var shapes = new List<StickButtonShape>
        {
            new(1, [new(181, 17), new(219, 17), new(222, 37), new(178, 37)]),
            new(2, [new(185, 121), new(212, 117), new(225, 147), new(206, 165), new(182, 150)]),
            new(3, [new(142, 71), new(165, 65), new(180, 113), new(163, 133), new(145, 111)]),
            new(4, [new(258, 71), new(235, 65), new(220, 113), new(237, 133), new(255, 111)])
        };
        int[] left = leftHanded ? [11, 12, 13, 16, 15, 14] : [5, 6, 7, 10, 9, 8];
        // Both arrays describe the visible rows from left to right.
        int[] right = leftHanded ? [7, 6, 5, 8, 9, 10] : [13, 12, 11, 14, 15, 16];
        for (int i = 0; i < 6; i++)
        {
            float x = 30 + (i % 3) * 40;
            float y = 205 + (i % 3) * 15 + (i / 3) * 45;
            PointF[] outline = [new(x, y), new(x + 32, y + 12), new(x + 38, y + 49), new(x + 6, y + 37)];
            shapes.Add(new(left[i], outline));
            // Mirroring the shape reverses its column, so use that visible column's number.
            int rightIndex = (i / 3) * 3 + (2 - i % 3);
            shapes.Add(new(right[rightIndex], outline.Select(p => new PointF(400 - p.X, p.Y)).ToArray()));
        }
        return shapes;
    }
}

public sealed class PhysicalButtonCapture
{
    private uint _previous;
    public bool IsArmed { get; private set; }
    public void Arm(uint current) { _previous = current; IsArmed = true; }
    public void Cancel() => IsArmed = false;
    public int Observe(uint buttons)
    {
        if (!IsArmed) return 0;
        uint pressed = buttons & ~_previous & 0xffff;
        _previous = buttons;
        if (pressed == 0 || (pressed & (pressed - 1)) != 0) return 0;
        IsArmed = false;
        return BitOperations.TrailingZeroCount(pressed) + 1;
    }
}

public sealed class T16000ButtonMap : Panel
{
    private const float DesignWidth = 400, DesignHeight = 460;
    private readonly Dictionary<int, DiagramButton> _buttons = new();
    private bool _leftHanded, _hasState;
    private uint _pressed;
    private int _selected;
    private int _additional;
    public event Action<int>? ButtonSelected;

    public T16000ButtonMap(bool allowSelection = true)
    {
        DoubleBuffered = true;
        MinimumSize = new Size(240, 270);
        AccessibleName = "T.16000M button layout, top view";
        for (int number = 1; number <= 16; number++)
        {
            int value = number;
            var button = new DiagramButton
            {
                Text = number.ToString(), AccessibleName = $"Button {number}",
                Cursor = allowSelection ? Cursors.Hand : Cursors.Default, TabIndex = number - 1,
                TabStop = allowSelection
            };
            button.Click += (_, _) => { if (allowSelection) ButtonSelected?.Invoke(value); };
            _buttons.Add(number, button);
            Controls.Add(button);
        }
    }

    private static GraphicsPath RoundedPolygon(PointF[] points, float radius)
    {
        var path = new GraphicsPath();
        for (int i = 0; i < points.Length; i++)
        {
            var previous = points[(i + points.Length - 1) % points.Length];
            var corner = points[i];
            var next = points[(i + 1) % points.Length];
            static PointF Along(PointF from, PointF to, float distance)
            {
                float length = MathF.Sqrt(MathF.Pow(to.X - from.X, 2) + MathF.Pow(to.Y - from.Y, 2));
                float t = length == 0 ? 0 : Math.Min(distance / length, 0.35f);
                return new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            }
            var entry = Along(corner, previous, radius);
            var exit = Along(corner, next, radius);
            if (i == 0) path.StartFigure();
            else path.AddLine(path.GetLastPoint(), entry);
            path.AddBezier(entry, corner, corner, exit);
        }
        path.CloseFigure();
        return path;
    }

    private sealed class DiagramButton : Button
    {
        private GraphicsPath? _shape;
        private bool _hover;
        public bool Assigned;
        public bool Pressed;

        public DiagramButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
        }

        public void SetOutline(PointF[] points)
        {
            _shape?.Dispose();
            _shape = RoundedPolygon(points, 2.5f);
            var oldRegion = Region;
            Region = new Region(_shape);
            oldRegion?.Dispose();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_shape is null || Width <= 0 || Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color top = Pressed ? Color.FromArgb(255, 187, 72) : Assigned ? Color.FromArgb(102, 167, 255)
                : _hover ? Color.FromArgb(240, 245, 251) : Color.FromArgb(207, 215, 224);
            Color bottom = Pressed ? Color.FromArgb(234, 119, 24) : Assigned ? Color.FromArgb(42, 96, 200)
                : _hover ? Color.FromArgb(179, 196, 212) : Color.FromArgb(139, 152, 167);
            using var fill = new LinearGradientBrush(ClientRectangle, top, bottom, 90f);
            e.Graphics.FillPath(fill, _shape);
            using var border = new Pen(Assigned ? Color.FromArgb(185, 219, 255) : Color.FromArgb(46, 54, 64), Assigned ? 2.5f : 1.5f);
            e.Graphics.DrawPath(border, _shape);
            Color text = Assigned && !Pressed ? Color.White : Color.FromArgb(21, 30, 43);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), text, bottom);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _shape?.Dispose();
            base.Dispose(disposing);
        }
    }

    public void ShowState(bool leftHanded, int selected, uint pressed, int additional = 0)
    {
        bool layoutChanged = _leftHanded != leftHanded;
        if (layoutChanged) { _leftHanded = leftHanded; PerformLayout(); }
        if (_hasState && !layoutChanged && _pressed == pressed && _selected == selected && _additional == additional) return;
        _hasState = true;
        _pressed = pressed;
        _selected = selected;
        _additional = additional;
        foreach (var (number, button) in _buttons)
        {
            button.Pressed = (pressed & (1u << (number - 1))) != 0;
            button.Assigned = number == selected || number == additional;
            button.Invalidate();
        }
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        float scale = Math.Min(ClientSize.Width / DesignWidth, ClientSize.Height / DesignHeight);
        if (scale <= 0) return;
        float offsetX = (ClientSize.Width - DesignWidth * scale) / 2;
        float offsetY = (ClientSize.Height - DesignHeight * scale) / 2;
        foreach (var shape in T16000Layout.Shapes(_leftHanded))
        {
            if (!_buttons.TryGetValue(shape.Number, out var button)) continue;
            float x = shape.Outline.Min(p => p.X), y = shape.Outline.Min(p => p.Y);
            int left = (int)MathF.Floor(offsetX + x * scale), top = (int)MathF.Floor(offsetY + y * scale);
            int right = (int)MathF.Ceiling(offsetX + shape.Outline.Max(p => p.X) * scale);
            int bottom = (int)MathF.Ceiling(offsetY + shape.Outline.Max(p => p.Y) * scale);
            button.Bounds = Rectangle.FromLTRB(left, top, right, bottom);
            button.SetOutline(shape.Outline.Select(p => new PointF(offsetX + p.X * scale - left, offsetY + p.Y * scale - top)).ToArray());
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = Math.Min(ClientSize.Width / DesignWidth, ClientSize.Height / DesignHeight);
        if (scale <= 0) return;
        e.Graphics.TranslateTransform((ClientSize.Width - DesignWidth * scale) / 2, (ClientSize.Height - DesignHeight * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var seam = new Pen(Color.FromArgb(30, 35, 41), 2);
        using var edge = new Pen(Color.FromArgb(111, 122, 134), 1.5f);

        // Broad, bevelled base with the two banks of six buttons on its shoulders.
        using var body = new GraphicsPath();
        body.AddBezier(120, 158, 160, 141, 240, 141, 280, 158);
        body.AddLine(280, 158, 361, 183);
        body.AddBezier(361, 183, 384, 191, 390, 246, 387, 302);
        body.AddBezier(387, 302, 399, 360, 293, 409, 200, 426);
        body.AddBezier(200, 426, 107, 409, 1, 360, 13, 302);
        body.AddBezier(13, 302, 10, 246, 16, 191, 39, 183);
        body.AddLine(39, 183, 120, 158);
        body.CloseFigure();
        using var shadow = (GraphicsPath)body.Clone();
        using (var translation = new Matrix())
        {
            translation.Translate(0, 5); shadow.Transform(translation);
        }
        using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
        g.FillPath(shadowBrush, shadow);
        using var baseFill = new LinearGradientBrush(new RectangleF(0, 150, 400, 280), Color.FromArgb(83, 91, 101), Color.FromArgb(39, 44, 51), 85f);
        g.FillPath(baseFill, body);
        g.DrawPath(seam, body);
        g.DrawBezier(edge, 20, 316, 72, 360, 135, 386, 200, 400);
        g.DrawBezier(edge, 200, 400, 265, 386, 328, 360, 380, 316);
        g.DrawLine(seam, 26, 319, 151, 360);
        g.DrawLine(seam, 374, 319, 249, 360);

        // Recessed button-bank panels, shaped like the sloping base facets.
        using var bankFill = new SolidBrush(Color.FromArgb(44, 50, 59));
        foreach (bool mirror in new[] { false, true })
        {
            PointF[] bank = [new(26, 200), new(146, 240), new(158, 335), new(32, 291)];
            if (mirror) bank = bank.Select(p => new PointF(400 - p.X, p.Y)).ToArray();
            using var path = RoundedPolygon(bank, 9);
            g.FillPath(bankFill, path); g.DrawPath(edge, path);
        }

        // Orange twist collar, black boot and a handed palm rest.
        using var boot = new SolidBrush(Color.FromArgb(24, 29, 35));
        g.FillEllipse(boot, 142, 232, 116, 103);
        using var orange = new Pen(Color.FromArgb(226, 126, 43), 5);
        g.DrawArc(orange, 145, 236, 110, 93, 14, 152);
        g.DrawArc(edge, 145, 236, 110, 93, 196, 150);
        PointF[] palm = [new(158, 226), new(231, 230), new(257, 261), new(235, 302), new(176, 303), new(145, 279)];
        if (_leftHanded) palm = palm.Select(p => new PointF(400 - p.X, p.Y)).ToArray();
        using var palmPath = RoundedPolygon(palm, 10);
        using var palmFill = new LinearGradientBrush(new Rectangle(140, 225, 120, 80), Color.FromArgb(66, 73, 82), Color.FromArgb(27, 32, 39), 80f);
        g.FillPath(palmFill, palmPath); g.DrawPath(seam, palmPath);

        // The grip narrows into the boot; upper facets keep its silhouette readable.
        PointF[] grip = [new(143, 35), new(188, 9), new(242, 26), new(263, 70), new(257, 129),
            new(233, 163), new(222, 246), new(206, 273), new(179, 252), new(165, 164), new(140, 132), new(130, 78)];
        using var gripPath = RoundedPolygon(grip, 8);
        using var gripFill = new LinearGradientBrush(new Rectangle(125, 10, 145, 270), Color.FromArgb(75, 82, 92), Color.FromArgb(23, 28, 35), 12f);
        g.FillPath(gripFill, gripPath); g.DrawPath(seam, gripPath);
        using var facet = new SolidBrush(Color.FromArgb(88, 97, 109));
        g.FillPolygon(facet, [new PointF(143, 37), new(185, 16), new(174, 54), new(139, 69)]);
        using var lowerFacet = new SolidBrush(Color.FromArgb(44, 51, 62));
        g.FillPolygon(lowerFacet, [new PointF(169, 166), new(195, 187), new(204, 261), new(181, 245)]);
        g.DrawLine(edge, 235, 167, 222, 240);
        for (int i = 0; i < 4; i++) g.DrawLine(seam, 175, 199 + i * 10, 194, 213 + i * 10);

        // POV is not one of the numbered 1-16 action buttons.
        g.FillEllipse(Brushes.Black, 174, 48, 55, 58);
        using var povFill = new LinearGradientBrush(new Rectangle(178, 52, 48, 49), Color.FromArgb(121, 131, 143), Color.FromArgb(54, 63, 75), 70f);
        g.FillEllipse(povFill, 178, 52, 47, 49);
        g.DrawEllipse(edge, 178, 52, 47, 49);
        using var cap = new SolidBrush(Color.FromArgb(159, 170, 182));
        g.FillEllipse(cap, 186, 60, 31, 31);
        g.DrawLine(seam, 196, 65, 206, 65);
        g.DrawLine(seam, 201, 60, 201, 70);

        // Separate throttle rail and ridged cap at the front of the base.
        g.FillRectangle(boot, 190, 340, 20, 53);
        g.DrawRectangle(edge, 190, 340, 20, 53);
        using var sliderFill = new LinearGradientBrush(new Rectangle(178, 356, 44, 22), Color.FromArgb(173, 183, 196), Color.FromArgb(103, 116, 131), 90f);
        g.FillRectangle(sliderFill, 177, 358, 46, 23);
        g.DrawRectangle(seam, 177, 358, 46, 23);
        for (int i = 0; i < 3; i++) g.DrawLine(edge, 181, 363 + i * 5, 219, 363 + i * 5);
        using var labelFont = new Font(Font.FontFamily, 12, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(166, 177, 190));
        using var centered = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("T.16000M", labelFont, labelBrush, new PointF(200, 397), centered);
        using var smallFont = new Font(Font.FontFamily, 9);
        g.DrawString("TOP VIEW  /  PLAYER BELOW", smallFont, Brushes.DimGray, new PointF(200, 437), centered);
    }
}
