using System.Drawing;
using System.Windows.Forms;

namespace WebView2Host;

/// <summary>
/// Startup splash: centered logo and title with loading progress at the bottom.
/// It appears before Program.Main probes or starts the backend, then advances through engine readiness, WebView2 initialization,
/// and page loading. The main form closes it after NavigationCompleted; a 20-second timeout shows a dismissible error.
/// The icon comes from Release.json icons.webui through the same path as the main form, falling back to the system icon.
/// </summary>
public sealed class SplashForm : Form
{
    private static readonly Color Transparent = Color.Magenta;
    private static readonly Color Title = Color.FromArgb(0xEC, 0xEE, 0xF1);
    private static readonly Color Muted = Color.FromArgb(0x9A, 0xA3, 0xAE);
    private static readonly Color Track = Color.FromArgb(0x33, 0x3A, 0x42);
    private static readonly Color Fill = Color.FromArgb(0xE5, 0x48, 0x4D);

    /// <summary>Minimum display time ensures the logo remains visible even during very fast loading.</summary>
    private const int MinShowMs = 500;
    /// <summary>Timeout fallback allows progress regardless of the current loading step (#26).</summary>
    private const int SafetyMs = 20_000;

    private Bitmap? _logo;
    private int _pct;
    private string _status = "";
    private long _shownAt;
    private bool _closing;
    private bool _timedOut;
    private readonly Rectangle _targetWorkArea;
    private readonly System.Windows.Forms.Timer _safety = new() { Interval = SafetyMs };

    internal SplashForm()
    {
        _targetWorkArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = _targetWorkArea.Location;
        ShowInTaskbar = false;
        BackColor = Transparent;
        TransparencyKey = Transparent;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        LoadLogo();
        _safety.Tick += (_, _) =>
        {
            _safety.Stop();
            _timedOut = true;
            _pct = 100;
            _status = ShellText.Pick("启动超时。请检查后端；单击此处可关闭提示。", "Startup timed out. Check the backend; click here to dismiss.");
            Cursor = Cursors.Hand;
            Invalidate();
        };
    }

    private double DpiScale => DeviceDpi <= 0 ? 1.0 : DeviceDpi / 96.0;
    private int Px(int dip) => (int)Math.Round(dip * DpiScale);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ClientSize = new Size(Px(400), Px(280));
        CenterIn(_targetWorkArea);
    }

    private void CenterIn(Rectangle work)
    {
        Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _shownAt = Environment.TickCount64;
        _safety.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_timedOut && e.Button == MouseButtons.Left) CloseNow();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Transparent);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var w = ClientSize.Width;
        var h = ClientSize.Height;

        // Logo, centered toward the top.
        var logoSz = Px(84);
        if (_logo != null)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_logo, (w - logoSz) / 2, Px(52), logoSz, logoSz);
        }

        // Title.
        using (var titleFont = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point))
        using (var titleBrush = new SolidBrush(Title))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
        {
            g.DrawString("HeartRateMonitor", titleFont, titleBrush,
                new RectangleF(0, Px(150), w, Px(28)), sf);
        }

        // Bottom: status text and progress bar.
        using (var statusFont = new Font("Segoe UI", 9.25f, GraphicsUnit.Point))
        using (var statusBrush = new SolidBrush(Muted))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
        {
            g.DrawString(_status, statusFont, statusBrush,
                new RectangleF(0, h - Px(56), w, Px(18)), sf);
        }
        var barW = Px(240);
        var barH = Px(3);
        var barX = (w - barW) / 2;
        var barY = h - Px(30);
        using var track = new SolidBrush(Track);
        g.FillRectangle(track, barX, barY, barW, barH);
        if (_pct > 0)
        {
            var fillW = (int)Math.Round(barW * Math.Clamp(_pct, 0, 100) / 100.0);
            if (fillW > 0) using (var fill = new SolidBrush(Fill)) g.FillRectangle(fill, barX, barY, fillW, barH);
        }
    }

    /// <summary>
    /// Advance from pct 0-100; retain the previous status when text is null. Safe to call from any thread.
    /// </summary>
    internal void Step(int pct, string? text)
    {
        void Apply()
        {
            _pct = Math.Clamp(pct, 0, 100);
            if (text != null) _status = text;
            Invalidate();
        }
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(Apply);
        else Apply();
    }

    /// <summary>Pump messages to repaint during blocking waits without a message loop, such as backend startup polling.</summary>
    internal void Pump()
    {
        if (IsDisposed) return;
        Application.DoEvents();
    }

    /// <summary>Close the splash after its minimum display time; repeated calls are safe.</summary>
    internal void RequestClose()
    {
        if (_closing || IsDisposed) return;
        var shownMs = _shownAt == 0 ? int.MaxValue : Environment.TickCount64 - _shownAt;
        if (shownMs >= MinShowMs) CloseNow();
        else
        {
            _closing = true;
            var wait = MinShowMs - (int)shownMs;
            var t = new System.Windows.Forms.Timer { Interval = Math.Max(50, wait) };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); CloseNow(); };
            t.Start();
        }
    }

    private void CloseNow()
    {
        _closing = true;
        _safety.Stop();
        _safety.Dispose();
        try { Close(); } catch { /* Already closing. */ }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _logo?.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Logo sources: manifest icons.webui, icons.app, image/app.ico, then the system icon.</summary>
    private void LoadLogo()
    {
        foreach (var p in new[] { MainForm.ManifestIconPath("webui"), MainForm.ManifestIconPath("app"), LegacyIconPath() })
        {
            if (p == null || !File.Exists(p)) continue;
            try
            {
                using var ico = new Icon(p, new Size(96, 96));
                _logo = (Bitmap)ico.ToBitmap().Clone();
                return;
            }
            catch { /* Invalid icon; continue falling back. */ }
        }
        try
        {
            using var ico = SystemIcons.Application;
            _logo = (Bitmap)ico.ToBitmap().Clone();
        }
        catch { /* If the system icon is unavailable, display text only. */ }
    }

    private static string? LegacyIconPath() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "image", "app.ico"))
            ? Path.Combine(AppContext.BaseDirectory, "image", "app.ico")
            : null;
}
