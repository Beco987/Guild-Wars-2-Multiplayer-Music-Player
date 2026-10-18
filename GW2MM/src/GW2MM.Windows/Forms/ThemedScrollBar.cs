using System.Runtime.InteropServices;

namespace GW2MM.Windows.Forms;

/// <summary>
/// A thin themed scrollbar laid over a control's own vertical scrollbar (lists, the chat, text boxes, the Data
/// Editor grid, scrolling panels). The native bar keeps doing the scrolling; this one only covers it, mirrors its
/// position and scrolls the control when dragged, clicked or wheeled. Attach with <see cref="Attach"/>.
/// </summary>
internal sealed class ThemedScrollBar : Control
{
    private readonly Control _target;
    private readonly Watcher _watcher;
    private (bool Shown, double Pos, double Size) _state;
    private bool _hover, _dragging;
    private int _dragOffset;

    private ThemedScrollBar(Control target)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Visible = false;
        _target = target;
        _watcher = new Watcher(this);
        if (target.IsHandleCreated) _watcher.AssignHandle(target.Handle);
        target.HandleCreated += (_, _) => _watcher.AssignHandle(target.Handle);
        target.HandleDestroyed += (_, _) => _watcher.ReleaseHandle();
        target.LocationChanged += (_, _) => Sync();
        target.SizeChanged += (_, _) => Sync();
        target.VisibleChanged += (_, _) => Sync();
        target.ParentChanged += (_, _) => Place();
        target.Disposed += (_, _) => Dispose();
        Place();
    }

    /// <summary>Covers <paramref name="target"/>'s vertical scrollbar with a themed one.</summary>
    public static void Attach(Control target) => _ = new ThemedScrollBar(target);

    private void Place()
    {
        if (Parent == _target.Parent) return;
        Parent?.Controls.Remove(this);
        if (_target.Parent is null) return;
        _target.Parent.Controls.Add(this);
        BringToFront();
        Sync();
    }

    /// <summary>Follows the native bar: where it is, whether it shows, and the thumb's position and size.</summary>
    private void Sync()
    {
        if (IsDisposed || _target.Parent is null || !_target.IsHandleCreated) return;
        var native = NativeBar();
        Visible = _target.Visible && native is not null;
        if (native is not { } r) return;
        if (Bounds != r) Bounds = r;
        if (Parent?.Controls.GetChildIndex(this) != 0) BringToFront();
        var state = ReadState();
        if (state != _state)
        {
            _state = state;
            Invalidate();
        }
    }

    /// <summary>The native vertical scrollbar's rectangle in the parent's coordinates (null when there is none).</summary>
    private Rectangle? NativeBar()
    {
        if (_target is DataGridView grid)
        {
            var bar = ((ThemedGrid)grid).VerticalBar;
            return bar.Visible ? _target.Parent!.RectangleToClient(bar.RectangleToScreen(bar.ClientRectangle)) : null;
        }
        if ((GetWindowLong(_target.Handle, GWL_STYLE) & WS_VSCROLL) == 0) return null;
        // The bar sits just right of the client area, as tall as it.
        var client = _target.Parent!.RectangleToClient(_target.RectangleToScreen(_target.ClientRectangle));
        return new Rectangle(client.Right, client.Top, SystemInformation.VerticalScrollBarWidth, client.Height);
    }

    /// <summary>Thumb position (0..1 of the free track) and size (0..1 of the track); Size 1 means nothing to scroll.</summary>
    private (bool, double, double) ReadState()
    {
        double min, max, page, pos;
        if (_target is DataGridView grid)
        {
            var bar = ((ThemedGrid)grid).VerticalBar;
            (min, max, page, pos) = (bar.Minimum, bar.Maximum, bar.LargeChange, bar.Value);
        }
        else
        {
            var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            if (!GetScrollInfo(_target.Handle, SB_VERT, ref si)) return (Visible, 0, 1);
            (min, max, page, pos) = (si.nMin, si.nMax, si.nPage, si.nPos);
        }
        var range = max - min + 1;
        if (page <= 0 || range <= page) return (Visible, 0, 1);
        return (Visible, Math.Clamp((pos - min) / (range - page), 0, 1), page / range);
    }

    private RectangleF Thumb()
    {
        var track = Track();
        var length = Math.Max(Theme.Sf(24), (float)(track.Height * _state.Size));
        var y = track.Y + (float)((track.Height - length) * _state.Pos);
        var w = Theme.Sf(_hover || _dragging ? 8 : 6);
        return new RectangleF((Width - w) / 2f, y, w, length);
    }

    private RectangleF Track() => new(0, Theme.Sf(3), Width, Height - Theme.Sf(6));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_target.BackColor);
        if (_state.Size >= 1) return;
        Theme.Smooth(g);
        var thumb = Thumb();
        Theme.FillRound(g, thumb, thumb.Width / 2, _dragging ? Theme.Azure : _hover ? Theme.Muted : Theme.BorderStrong);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _state.Size >= 1) return;
        var thumb = Thumb();
        if (e.Y >= thumb.Top && e.Y < thumb.Bottom)
        {
            _dragging = true;
            _dragOffset = e.Y - (int)thumb.Top;
            Capture = true;
            Invalidate();
        }
        else
        {
            // A click on the track moves a page towards the pointer.
            var step = _state.Size / (1 - _state.Size);
            ScrollTo(_state.Pos + (e.Y < thumb.Top ? -step : step));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var track = Track();
        var free = track.Height - Thumb().Height;
        if (free > 0) ScrollTo((e.Y - _dragOffset - track.Y) / free);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        // The wheel over the bar scrolls the control beneath it.
        if (m.Msg == WM_MOUSEWHEEL && _target.IsHandleCreated)
        {
            SendMessage(_target.Handle, m.Msg, m.WParam, m.LParam);
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Scrolls the control so the thumb sits at <paramref name="fraction"/> (0 = top, 1 = bottom).</summary>
    private void ScrollTo(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        switch (_target)
        {
            case ListView list when list.Items.Count > 0:
            {
                var perPage = (int)SendMessage(list.Handle, LVM_GETCOUNTPERPAGE, 0, 0);
                var top = (int)Math.Round(fraction * Math.Max(0, list.Items.Count - perPage));
                var current = (int)SendMessage(list.Handle, LVM_GETTOPINDEX, 0, 0);
                SendMessage(list.Handle, LVM_SCROLL, 0, (top - current) * list.GetItemRect(0).Height);
                break;
            }
            case DataGridView grid:
            {
                var rows = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).ToList();
                var shown = grid.DisplayedRowCount(false);
                if (rows.Count == 0) break;
                grid.FirstDisplayedScrollingRowIndex = rows[(int)Math.Round(fraction * Math.Max(0, rows.Count - shown))].Index;
                break;
            }
            case ScrollableControl panel:
            {
                var si = Info();
                panel.AutoScrollPosition = new Point(-panel.AutoScrollPosition.X, si.nMin + (int)Math.Round(fraction * (si.nMax - si.nMin + 1 - si.nPage)));
                break;
            }
            case RichTextBox rich:
            {
                var si = Info();
                var p = new Point(0, si.nMin + (int)Math.Round(fraction * (si.nMax - si.nMin + 1 - si.nPage)));
                SendMessage(rich.Handle, EM_SETSCROLLPOS, 0, ref p);
                break;
            }
            case TextBoxBase text:
            {
                var si = Info();
                var line = si.nMin + (int)Math.Round(fraction * (si.nMax - si.nMin + 1 - si.nPage));
                SendMessage(text.Handle, EM_LINESCROLL, 0, line - si.nPos);
                break;
            }
            case ListBox box:
            {
                var si = Info();
                box.TopIndex = si.nMin + (int)Math.Round(fraction * (si.nMax - si.nMin + 1 - si.nPage));
                break;
            }
        }
        Sync();
    }

    private SCROLLINFO Info()
    {
        var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
        GetScrollInfo(_target.Handle, SB_VERT, ref si);
        return si;
    }

    /// <summary>Watches the control's messages and re-syncs after anything that can move or show its scrollbar.</summary>
    private sealed class Watcher : NativeWindow
    {
        private readonly ThemedScrollBar _owner;
        public Watcher(ThemedScrollBar owner) => _owner = owner;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is WM_PAINT or WM_VSCROLL or WM_MOUSEWHEEL or WM_SIZE or WM_KEYDOWN or WM_STYLECHANGED or WM_WINDOWPOSCHANGED)
                _owner.Sync();
        }
    }

    private const int GWL_STYLE = -16, WS_VSCROLL = 0x00200000, SB_VERT = 1, SIF_ALL = 0x17;
    private const int WM_SIZE = 0x0005, WM_PAINT = 0x000F, WM_KEYDOWN = 0x0100, WM_VSCROLL = 0x0115, WM_MOUSEWHEEL = 0x020A;
    private const int WM_STYLECHANGED = 0x007D, WM_WINDOWPOSCHANGED = 0x0047;
    private const int LVM_SCROLL = 0x1014, LVM_GETTOPINDEX = 0x1027, LVM_GETCOUNTPERPAGE = 0x1028;
    private const int EM_LINESCROLL = 0x00B6, EM_SETSCROLLPOS = 0x0400 + 222;

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize, fMask;
        public int nMin, nMax;
        public uint nPage;
        public int nPos, nTrackPos;
    }

    [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, int wParam, ref Point lParam);
}

/// <summary>A DataGridView whose vertical scrollbar a <see cref="ThemedScrollBar"/> can follow.</summary>
internal sealed class ThemedGrid : DataGridView
{
    public ScrollBar VerticalBar => VerticalScrollBar;
}
