using System.Drawing.Drawing2D;
using System.Reflection;
using System.ComponentModel;

namespace BAAxis;

internal sealed partial class MainForm
{
    private static readonly Color Ink = Color.FromArgb(43, 70, 94);
    private static readonly Color Cyan = Color.FromArgb(0, 166, 222);
    private Bitmap? backdrop;
    private readonly Image artwork = LoadArtwork();

    private static Image LoadArtwork(string name="background.png")
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BAAxis.Assets."+name)!;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private void InitializeThemeLayout()
    {
        SuspendLayout();
        Text = "Auto-宏";
        using (var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BAAxis.Assets.app.ico")!)
        using (var icon=new Icon(stream)) Icon=(Icon)icon.Clone();
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);
        ForeColor = Ink;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(239, 248, 253);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(24, 12, 24, 16), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 312));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 18) };
        var appBadge=new PictureBox {Image=LoadArtwork("app-icon.png"),SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(0,6),Size=new Size(52,52),BackColor=Color.Transparent};
        header.Controls.Add(appBadge);
        var title = new Label { Text = "Auto-宏", AutoSize = true, Font = new Font(Font.FontFamily, 24, FontStyle.Bold), Location = new Point(64, 6), BackColor = Color.Transparent };
        header.Controls.Add(title);
        var files = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 405, Padding = new Padding(0, 16, 0, 0), BackColor = Color.Transparent, WrapContents = false };
        void AddButton(FlowLayoutPanel host, string text, Action action, bool primary = false)
        {
            var button = new AcademyButton { Text = text, Width = 91, Height = 42, Primary = primary, Margin = new Padding(4, 0, 4, 0) };
            button.Click += (_, _) => Guard(action);
            host.Controls.Add(button);
        }
        AddButton(files, "新建", () => { Idle(); document = new(); filePath = null; Bind(); });
        AddButton(files, "打开", Open);
        AddButton(files, "保存", Save, true);
        AddButton(files, "另存", () => { filePath = null; Save(); });
        header.Controls.Add(files);
        layout.Controls.Add(header, 0, 0); layout.SetColumnSpan(header, 2);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 0, 20, 12) };
        var settings = new GlassPanel { Dock = DockStyle.Top, Height = 326, Padding = new Padding(20), Tint = Color.FromArgb(210, 255, 255, 255) };
        settings.Controls.Add(new Label { Text = "执行设置", AutoSize = true, Location = new Point(20, 18), Font = new Font(Font, FontStyle.Bold) });
        void Field(string label, Control control, int y)
        {
            settings.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(20, y), BackColor = Color.Transparent });
            control.Location = new Point(20, y + 25); control.Width = 208;
            control.Font = new Font("Consolas", 13);
            control.ForeColor = Ink;
            control.BackColor = Color.FromArgb(246, 251, 255);
            settings.Controls.Add(control);
        }
        Field("总时长", total, 58); Field("收轴剩余", end, 140); Field("次数上限", count, 222);
        BuildEndControls(settings);
        sidebar.AutoScroll=true;
        sidebar.Controls.Add(settings);
        layout.Controls.Add(sidebar, 0, 1);

        var editor = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 12), Tint = Color.FromArgb(130, 255, 255, 255) };
        var caption = new GlassPanel { Dock = DockStyle.Top, Height = 52, Tint = Color.FromArgb(225, 255, 255, 255) };
        caption.Controls.Add(new Label { Text = "录制操作（只读）", AutoSize = true, Location = new Point(18, 15), BackColor = Color.Transparent, Font = new Font(Font, FontStyle.Bold) });
        caption.Controls.Add(new Label { Text = "剩余时间 · 分:秒.毫秒", Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, ForeColor = Color.FromArgb(93, 118, 138) });
        StyleGrid();
        editor.Controls.Add(BuildAxisTabs()); editor.Controls.Add(caption);
        layout.Controls.Add(editor, 1, 1);

        var footerColor = Color.FromArgb(247, 251, 255);
        var footer = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), Margin = Padding.Empty, BackColor = footerColor, Tint = Color.Transparent };
        var footerContent = new BufferedTablePanel { Dock = DockStyle.Fill, BackColor = footerColor, RowCount = 3, ColumnCount = 1 };
        footerContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        footerContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        footerContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timer.ForeColor = Cyan; timer.Font = new Font(Font, FontStyle.Bold); timer.Margin = Padding.Empty;
        timer.AutoSize = false; timer.Dock = DockStyle.Fill; timer.BackColor = footerColor;
        status.Margin = Padding.Empty; status.AutoEllipsis = true; status.AutoSize = false; status.Dock = DockStyle.Fill;
        status.BackColor = footerColor;
        var help = new BufferedLabel { Text = "录下前置重开和战斗操作，结束后选中跳过动画的确认键行，标记开战零点。", Dock = DockStyle.Fill, BackColor = footerColor, AutoEllipsis = true, ForeColor = Color.FromArgb(93, 118, 138), Font = new Font(Font.FontFamily, 9), Margin = Padding.Empty };
        footerContent.Controls.Add(timer, 0, 0); footerContent.Controls.Add(status, 0, 1); footerContent.Controls.Add(help, 0, 2);
        footer.Controls.Add(footerContent);
        layout.Controls.Add(footer, 0, 2); layout.SetColumnSpan(footer, 2);
        Controls.Add(layout);
        Resize += (_, _) => { backdrop?.Dispose(); backdrop = null; Invalidate(true); };
        Disposed += (_, _) => { backdrop?.Dispose(); artwork.Dispose(); appBadge.Image?.Dispose(); Icon?.Dispose(); };
        ResumeLayout();
    }

    private void StyleGrid()
    {
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Color.FromArgb(239, 248, 253);
        grid.GridColor = Color.FromArgb(218, 233, 243);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(224, 243, 252), ForeColor = Ink, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6, 8, 6, 8), SelectionBackColor = Color.FromArgb(224, 243, 252), SelectionForeColor = Ink };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(250, 253, 255), ForeColor = Ink, SelectionBackColor = Color.FromArgb(183, 231, 250), SelectionForeColor = Color.FromArgb(24, 67, 95), Padding = new Padding(5, 7, 5, 7), Font = new Font("Consolas", 11) };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(236, 247, 253);
        grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(229, 243, 251);
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(183, 231, 250);
        grid.RowHeadersWidth = 30;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (artwork == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) { base.OnPaintBackground(e); return; }
        if (backdrop == null)
        {
            backdrop = new Bitmap(ClientSize.Width, ClientSize.Height);
            using var g = Graphics.FromImage(backdrop);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Max(ClientSize.Width / (float)artwork.Width, ClientSize.Height / (float)artwork.Height);
            float width = artwork.Width * scale, height = artwork.Height * scale;
            g.DrawImage(artwork, (ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
            using var wash = new SolidBrush(Color.FromArgb(35, 250, 253, 255));
            g.FillRectangle(wash, ClientRectangle);
        }
        e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
    }

    internal void PaintArtwork(Graphics graphics, Control control, Rectangle region)
    {
        if (backdrop == null) return;
        var origin = PointToClient(control.PointToScreen(Point.Empty));
        graphics.DrawImage(backdrop, region, new Rectangle(origin.X + region.X, origin.Y + region.Y, region.Width, region.Height), GraphicsUnit.Pixel);
    }
}

internal sealed class GlassPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Tint { get; set; } = Color.FromArgb(180, 255, 255, 255);
    public GlassPanel() { DoubleBuffered = true; BackColor = Color.Transparent; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var fill = new SolidBrush(Tint);
        e.Graphics.FillRectangle(fill, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(180, 210, 232, 245));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class BufferedLabel : Label
{
    public BufferedLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}

internal sealed class BufferedTablePanel : TableLayoutPanel
{
    public BufferedTablePanel() { DoubleBuffered = true; }
}

internal sealed class AxisGrid : DataGridView
{
    public AxisGrid() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int bottom = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
        int first = Math.Max(0, FirstDisplayedScrollingRowIndex);
        for (int i = first; i < Rows.Count && Rows[i].Displayed; i++)
            bottom = Math.Max(bottom, GetRowDisplayRectangle(i, true).Bottom);
        int right = Controls.OfType<VScrollBar>().FirstOrDefault(s => s.Visible)?.Left ?? ClientSize.Width;
        int lower = Controls.OfType<HScrollBar>().FirstOrDefault(s => s.Visible)?.Top ?? ClientSize.Height;
        var empty = Rectangle.FromLTRB(0, bottom, right, lower);
        if (empty.Height <= 0 || FindForm() is not MainForm form) return;
        form.PaintArtwork(e.Graphics, this, empty);
        using var tint = new SolidBrush(Color.FromArgb(145, 249, 253, 255));
        e.Graphics.FillRectangle(tint, empty);
    }
}

internal sealed class AcademyButton : Button
{
    [DefaultValue(false)]
    public bool Primary { get; set; }
    private bool hovered, pressed;
    public AcademyButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.Transparent;
        ForeColor = Color.FromArgb(43, 70, 94); Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (FindForm() is MainForm form) form.PaintArtwork(e.Graphics, this, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Point[] shape = [new(1, 1), new(Width-2, 1), new(Width-2, Height-2), new(1, Height-2)];
        var color = Primary ? Color.FromArgb(91, 213, 251) : Color.FromArgb(239, 249, 255);
        if (hovered || pressed) color = Primary ? Color.FromArgb(41, 192, 242) : Color.FromArgb(211, 240, 254);
        using var brush = new SolidBrush(color);
        var borderColor = Focused ? Color.FromArgb(26, 122, 171) : Primary ? Color.FromArgb(35, 178, 226) : Color.FromArgb(177, 212, 232);
        using var pen = new Pen(borderColor, Focused ? 2 : 1);
        e.Graphics.FillPolygon(brush, shape); e.Graphics.DrawPolygon(pen, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}


