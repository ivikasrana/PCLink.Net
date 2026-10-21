using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PCLink.Net;

public sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon icon;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? SendClipboardRequested;
    public event Action? SyncRequested;

    public Tray()
    {
        icon = new Forms.NotifyIcon { Icon = MakeIcon(), Visible = true, Text = "PCLink" };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Send clipboard to phone", null, (_, _) => SendClipboardRequested?.Invoke());
        menu.Items.Add("Sync now", null, (_, _) => SyncRequested?.Invoke());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void SetText(string text) => icon.Text = text.Length > 60 ? text[..60] : text;

    public void Balloon(string title, string body) => icon.ShowBalloonTip(4000, title, body, Forms.ToolTipIcon.Info);

    private static Drawing.Icon MakeIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);
        using var blue = new Drawing.SolidBrush(Drawing.Color.FromArgb(0, 122, 255));
        using var white = new Drawing.SolidBrush(Drawing.Color.White);
        g.FillEllipse(blue, 1, 1, 30, 30);
        g.FillRectangle(white, 11, 6, 10, 20);
        g.FillRectangle(blue, 13, 9, 6, 11);
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
