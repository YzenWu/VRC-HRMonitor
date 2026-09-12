using System.Runtime.InteropServices;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.UI;

/// <summary>
/// Suspended windows (locking = top + click through to show {bpm}/{img}/text).
/// There is always no primary topbar: Left drags, right-key menu operations, double-click to open the main interface, bottom right drags.
/// </summary>
public class FloatingWindow : Form
{
    private readonly string _id;
    private readonly Func<int> _bpmProvider;
    private readonly Action<string> _onClosed;
    public bool Locked { get; private set; }
    internal event Action? GeometryChanged;
    internal string Id => _id;
    internal string Geometry
    {
        get
        {
            var scale = Math.Max(0.01, DeviceDpi / 96.0);
            var width = (int)Math.Round(ClientSize.Width / scale);
            var height = (int)Math.Round(ClientSize.Height / scale);
            return $"{width}x{height}+{Left}+{Top}";
        }
    }

    private string _format = "❤️{bpm}";
    private string _unlockedColor = "#00FF00";
    private string _lockedColor = "#FF6600";
    private Image? _image;

    private readonly FlowLayoutPanel _flow;
    private readonly Panel _resizeGrip;
    private readonly List<Label> _bpmLabels = new();
    private readonly List<PictureBox> _imgBoxes = new();
    private readonly List<Label> _textLabels = new();

    /// <summary> manual drag state: Without WM_NCLBUTTONDOWN, its modular drag cycle would eat double-click. </summary>
    private bool _dragging;
    private Point _dragCursor;
    private Point _dragOrigin;

    /// <summary> locks in state colour: The pixels of the colour are fully transparent and the mouse penetrates. </summary>
    static readonly Color KeyColor = Color.Black;
    /// <summary> unlocked background: Visible and dragable to avoid a "undetectable or invisible" semi-locked view. </summary>
    static readonly Color UnlockedBack = Color.FromArgb(32, 32, 32);
    /// <summary> right bottom corner zooms in heat (logical pixels). </summary>
    const int GripSize = 14;

    public FloatingWindow(string id, string title, Func<int> bpmProvider, Action<string> onClosed)
    {
        _id = id;
        _bpmProvider = bpmProvider;
        _onClosed = onClosed;
        Text = title;
        // Colour keys fixed to black: only when BackColor is locked is it really transparent + penetrator
        BackColor = UnlockedBack;
        TransparencyKey = KeyColor;
        StartPosition = FormStartPosition.Manual;
        // Never show the original topbar: scale to WM_NCHITTEST
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        // The minimum size must be scaled up by DPI: otherwise, the high DPI is smaller than the minimum tracking size of the system and is ineffective
        MinimumSize = new Size((int)(120 * DeviceDpi / 96.0), (int)(40 * DeviceDpi / 96.0));
        ApplyGeometry(App.Config.HeartRate.Window.Geometry);
        ContextMenuStrip = BuildMenu();

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        Controls.Add(_flow);
        _resizeGrip = new Panel
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.Transparent,
            Cursor = Cursors.SizeNWSE,
            Size = new Size(GripSize, GripSize),
            Location = new Point(ClientSize.Width - GripSize, ClientSize.Height - GripSize),
        };
        _resizeGrip.Paint += PaintResizeGrip;
        _resizeGrip.MouseDown += (_, e) =>
        {
            if (Locked || e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0);
        };
        Controls.Add(_resizeGrip);
        _resizeGrip.BringToFront();
        Resize += (_, _) =>
        {
            UpdateFonts();
            _resizeGrip.Location = new Point(ClientSize.Width - _resizeGrip.Width, ClientSize.Height - _resizeGrip.Height);
        };
        HookMouse(this);
        HookMouse(_flow);
    }

    /// <summary> right-key menu: Replaces all operational entrances of the original top bar. </summary>
    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var lockItem = new ToolStripMenuItem("锁定", null, (_, _) =>
        {
            var next = !App.Config.HeartRate.Window.Locked;
            App.Config.HeartRate.Window.Locked = next;
            App.Config.Save();
            FloatWindowHost.ToggleLockAll(next);
        });
        menu.Items.Add(lockItem);
        menu.Items.Add("打开主界面", null, (_, _) => ProcessInfo.LaunchFrontend());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关闭此窗", null, (_, _) => Close());
        menu.Items.Add("关闭全部悬浮窗", null, (_, _) => FloatWindowHost.CloseAll());
        // Refresh lock entry text before opening (state may be modified by tray/frontend)
        menu.Opening += (_, _) => lockItem.Text = Locked ? "解锁" : "锁定";
        return menu;
    }

    /// <summary>
    /// Parsing the geometry configuration for "widespread +X+Y". 96 DPI logical pixels are understood and set to the client area:
    /// If Width/Height is directly set, the window below the high DPI will be squeezed by Windows to minimum trace size.
    /// The non-client area (headbar + border) crowds the client area into 0 height, making the window look empty and uncapable.
    /// (E3 internal: FloatWindowHost.ApplyStyle reset geometry for open main window)
    /// </summary>
    internal void ApplyGeometry(string geo)
    {
        var w = 220;
        var h = 60;
        var x = 100;
        var y = 100;
        var m = System.Text.RegularExpressions.Regex.Match(geo ?? "", @"^(\d+)x(\d+)\+(-?\d+)\+(-?\d+)$");
        if (m.Success)
        {
            w = int.Parse(m.Groups[1].Value);
            h = int.Parse(m.Groups[2].Value);
            x = int.Parse(m.Groups[3].Value);
            y = int.Parse(m.Groups[4].Value);
        }
        var scale = DeviceDpi / 96.0;
        ClientSize = new Size((int)(Math.Max(w, 60) * scale), (int)(Math.Max(h, 24) * scale));
        Location = new Point(x, y);
    }

    public void SetStyle(string format, string unlockedColor, string lockedColor, string? imagePath)
    {
        _format = format;
        _unlockedColor = unlockedColor;
        _lockedColor = lockedColor;
        _image = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath) ? LoadImage(imagePath) : null;
        Rebuild();
    }

    static Image? LoadImage(string path)
    {
        try { return Image.FromFile(path); } catch { return null; }
    }

    public void Rebuild()
    {
        _flow.Controls.Clear();
        _bpmLabels.Clear();
        _imgBoxes.Clear();
        _textLabels.Clear();

        var parts = System.Text.RegularExpressions.Regex.Split(_format ?? "", @"(\{bpm\}|\{img\})");
        foreach (var part in parts)
        {
            if (part == "{bpm}")
            {
                var lb = new Label { AutoSize = true, BackColor = Color.Transparent, ForeColor = ParseColor(_unlockedColor), Text = "--" };
                _flow.Controls.Add(lb);
                _bpmLabels.Add(lb);
                HookMouse(lb);
            }
            else if (part == "{img}")
            {
                var pb = new PictureBox { Size = new Size(64, 64), BackColor = Color.Transparent, SizeMode = PictureBoxSizeMode.Zoom };
                _flow.Controls.Add(pb);
                _imgBoxes.Add(pb);
                HookMouse(pb);
            }
            else if (part.Length > 0)
            {
                var lb = new Label { AutoSize = true, BackColor = Color.Transparent, ForeColor = ParseColor(_unlockedColor), Text = part };
                _flow.Controls.Add(lb);
                _textLabels.Add(lb);
                HookMouse(lb);
            }
        }
        UpdateFonts();
        UpdateHeartRate();
    }

    /// <summary>
    /// Unlock state: The left key holds down drag-and-form and double-click to open the main interface. The client area is covered by labels and needs to be linked on a case-by-case basis.
    /// Location instead of hairing WM_NCLBUTTONDOWN - the latter will enter the system's mode drag cycle.
    /// Eat the second click immediately after and double-click will never get it.
    /// </summary>
    private void HookMouse(Control c)
    {
        c.MouseDown += (_, e) =>
        {
            if (Locked || e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragCursor = Cursor.Position;
            _dragOrigin = Location;
        };
        c.MouseMove += (_, e) =>
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            var d = new Point(Cursor.Position.X - _dragCursor.X, Cursor.Position.Y - _dragCursor.Y);
            Location = new Point(_dragOrigin.X + d.X, _dragOrigin.Y + d.Y);
        };
        c.MouseUp += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            GeometryChanged?.Invoke();
        };
        c.DoubleClick += (_, _) =>
        {
            if (!Locked) ProcessInfo.LaunchFrontend();
        };
    }

    private void UpdateFonts()
    {
        // The font is a pound and is self-emplified with DPI, so it is based on logical pixels; the client area is high instead of the window (the latter contains the titlebar)
        var h = Math.Max((ClientSize.Height - 8) * 96 / DeviceDpi, 40);
        // Zoom high but capped with the word in the window to avoid zooming to full screen
        var bpmSize = Math.Clamp(h * 45 / 100, 12, 72);
        var textSize = Math.Clamp(h * 24 / 100, 9, 36);
        foreach (var lb in _bpmLabels) lb.Font = new Font("Arial", bpmSize, FontStyle.Bold);
        foreach (var lb in _textLabels) lb.Font = new Font("Microsoft YaHei UI", textSize);
        foreach (var pb in _imgBoxes)
        {
            var ih = Math.Max(ClientSize.Height - 8, 16);
            pb.Height = ih;
            pb.Width = _image != null ? (int)(ih * (double)_image.Width / Math.Max(1, _image.Height)) : ih;
            pb.Image = _image;
        }
    }

    public void UpdateHeartRate()
    {
        var bpm = _bpmProvider();
        foreach (var lb in _bpmLabels)
            lb.Text = bpm > 0 ? bpm.ToString() : "--";
    }

    public void ToggleLock(bool locked)
    {
        Locked = locked;
        // Lock: Top + Background to colour to make windows transparent + Click through
        // Unlock: Dark background (visible, dragable, pointable)
        // The border style is always None, so there is no need to compensate the non-client area for the movement of the client area/location
        BackColor = locked ? KeyColor : UnlockedBack;
        TopMost = locked;
        _resizeGrip.Visible = !locked;
        ApplyClickThrough(locked);
        UpdateColors();
        App.Log.Info(LogText.L(locked ? "log.float.locked" : "log.float.unlocked", _id));
    }

    private void UpdateColors()
    {
        var fg = Locked ? _lockedColor : _unlockedColor;
        foreach (var lb in _bpmLabels) lb.ForeColor = ParseColor(fg);
        foreach (var lb in _textLabels) lb.ForeColor = ParseColor(_unlockedColor);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _onClosed?.Invoke(_id);
        base.OnFormClosed(e);
    }

    const int WS_EX_TRANSPARENT = 0x20;

    /// <summary>
    /// Toggle WS_EX_TRANSPARENT only.
    /// WS_EX_LAYERED is self-maintained by WinForms based on TransparencyKey, and manual clearance undermines the transparency of colour keys.
    /// </summary>
    private void ApplyClickThrough(bool on)
    {
        if (!IsHandleCreated) return;
        var style = GetWindowLong(Handle, GWL_EXSTYLE);
        style = on ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, style);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Once the handle is rebuilt, we need to re-introduce the penetrator.
        ApplyClickThrough(Locked);
    }

    /// <summary>
    /// ZHTBOTTOMRIGHT on the lower right corner.
    /// Size will fight with MinimumSize, DPI.
    /// Lock-out not to get involved -- then the whole window goes through.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && !Locked)
        {
            var screen = new Point((short)(long)m.LParam, (short)((long)m.LParam >> 16));
            var p = PointToClient(screen);
            var grip = (int)(GripSize * DeviceDpi / 96.0);
            if (p.X >= ClientSize.Width - grip && p.Y >= ClientSize.Height - grip)
            {
                m.Result = (IntPtr)HTBOTTOMRIGHT;
                return;
            }
        }
        base.WndProc(ref m);
    }

    /// <summary> unlocks draw a zoom handle in the lower right corner, prompting to drag here. </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Locked) return;
        var grip = (int)(GripSize * DeviceDpi / 96.0);
        using var pen = new Pen(Color.FromArgb(110, 255, 255, 255));
        for (var i = 1; i <= 3; i++)
        {
            var off = i * grip / 4;
            e.Graphics.DrawLine(pen,
                ClientSize.Width - off, ClientSize.Height - 1,
                ClientSize.Width - 1, ClientSize.Height - off);
        }
    }

    private static void PaintResizeGrip(object? sender, PaintEventArgs e)
    {
        if (sender is not Control grip) return;
        using var pen = new Pen(Color.FromArgb(150, 255, 255, 255));
        for (var i = 1; i <= 3; i++)
        {
            var off = i * Math.Max(1, grip.Width / 4);
            e.Graphics.DrawLine(pen, grip.Width - off, grip.Height - 1, grip.Width - 1, grip.Height - off);
        }
    }

    static Color ParseColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6) return Color.FromArgb(Convert.ToInt32(hex, 16) | unchecked((int)0xFF000000));
            return Color.FromArgb(Convert.ToInt32(hex, 16));
        }
        catch { return Color.Lime; }
    }

    const int GWL_EXSTYLE = -20;
    const int WM_NCHITTEST = 0x84;
    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HTBOTTOMRIGHT = 17;
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
