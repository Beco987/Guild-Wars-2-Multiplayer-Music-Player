using GW2MM.Core.Config;
using GW2MM.Core.Library;
using GW2MM.Core.Sessions;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The main window's Playlists side: pick a playlist (the ⋯ menu creates, renames and deletes them; Shuffle and
/// Repeat are toggles beside it), see and reorder its songs, add songs from the library and play it from any song.
/// Playing is delegated to the host or client session through <see cref="PlayRequested"/>.
/// </summary>
internal sealed class PlaylistPanel : UserControl
{
    private readonly PlaylistStore _store;
    private readonly AppSettings _settings;
    private readonly string _dataDir;
    private readonly Func<IReadOnlyList<SongEntry>> _librarySongs;
    private readonly Action<string> _report;
    private readonly PlaylistPicker _picker;
    private readonly ThemedListView _list;
    private readonly ThemedButton _add, _remove, _up, _down, _shuffleButton, _repeatButton;
    private readonly ThemedButton? _next;
    private readonly ToolStripMenuItem _rename, _delete, _shuffle, _repeat;
    private readonly ThemedToolTip _tips = new();
    private Dictionary<string, SongEntry> _library = new(StringComparer.Ordinal);
    private Playlist? _current;
    private NowPlayingInfo? _nowPlaying;
    private bool _loading;

    /// <param name="next">Next: skips the playlist song that is playing (null for no Next button).</param>
    public PlaylistPanel(PlaylistStore store, AppSettings settings, string dataDir, Func<IReadOnlyList<SongEntry>> librarySongs,
        Action<string> report, Action? next)
    {
        _store = store;
        _settings = settings;
        _dataDir = dataDir;
        _librarySongs = librarySongs;
        _report = report;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Font = Theme.Body;

        _picker = new PlaylistPicker { Bounds = Theme.S(7, 7, 270, 30) };
        Controls.Add(_picker);
        _shuffleButton = Icon(Glyph.Shuffle, "Shuffle", 285, 7);
        _repeatButton = Icon(Glyph.Repeat, "Repeat", 319, 7);
        var more = Icon(Glyph.More, "More playlist actions", 357, 7);
        var menu = Menus.Create();
        menu.Item("New playlist…", Glyph.Plus, (_, _) => NewPlaylist());
        _rename = menu.Item("Rename…", Glyph.None, (_, _) => RenameCurrent());
        _delete = menu.Item("Delete playlist…", Glyph.Trash, (_, _) => DeleteCurrent(), danger: true);
        menu.Items.Add(new ToolStripSeparator());
        _shuffle = menu.Item("Shuffle", Glyph.None, null);
        _shuffle.CheckOnClick = true;
        _shuffle.Checked = settings.PlaylistShuffle;
        _repeat = menu.Item("Repeat", Glyph.None, null);
        _repeat.CheckOnClick = true;
        _repeat.Checked = settings.PlaylistRepeat;
        _shuffle.CheckedChanged += (_, _) => { _shuffleButton.Toggled = _shuffle.Checked; SaveOptions(); };
        _repeat.CheckedChanged += (_, _) => { _repeatButton.Toggled = _repeat.Checked; SaveOptions(); };
        _shuffleButton.Toggled = _shuffle.Checked;
        _repeatButton.Toggled = _repeat.Checked;
        _shuffleButton.Click += (_, _) => _shuffle.Checked = !_shuffle.Checked;
        _repeatButton.Click += (_, _) => _repeat.Checked = !_repeat.Checked;
        menu.Items.Add(new ToolStripSeparator());
        menu.Item("Open Playlists folder", Glyph.Folder, (_, _) => OpenFolder());
        more.Click += (_, _) => menu.Show(more, new Point(more.Width - menu.GetPreferredSize(Size.Empty).Width, more.Height));
        _tips.SetToolTip(_shuffleButton, "Shuffle");
        _tips.SetToolTip(_repeatButton, "Repeat");
        _tips.SetToolTip(more, "New, rename, delete, open the folder");

        _list = new ThemedListView { MultiSelect = true, AllowDrop = true, Bounds = Theme.S(3, 43, 388, 176), FillColumn = 1 };
        _list.Columns.Add("#", Theme.S(40), HorizontalAlignment.Right);
        _list.Columns.Add("Song", Theme.S(250));
        _list.Columns.Add("Players", Theme.S(80), HorizontalAlignment.Center);
        _list.Look = item => item.Tag switch
        {
            RowState { Playing: true } => new RowLook(Bold: true, Color: Theme.AzureText, Glyph: Glyph.Equalizer),
            RowState { Known: false } => new RowLook(Dim: true),
            _ => default,
        };
        Controls.Add(_list);
        _list.FitColumns();
        _list.DoubleClick += (_, _) => PlayRequested?.Invoke();
        // Enter must reach the list: the main window's default button (Send) would otherwise take it.
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { PlayRequested?.Invoke(); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Up) { MoveSelected(-1); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Down) { MoveSelected(1); e.Handled = true; }
        };
        _list.SelectedIndexChanged += (_, _) => { UpdateButtons(); SongSelected?.Invoke(SelectedSong); };
        _list.ItemDrag += (_, e) => { if (e.Button == MouseButtons.Left) _list.DoDragDrop(e.Item!, DragDropEffects.Move); };
        _list.DragOver += (_, e) => e.Effect = e.Data?.GetDataPresent(typeof(ListViewItem)) == true ? DragDropEffects.Move : DragDropEffects.None;
        _list.DragDrop += (_, e) => DropAt(_list.PointToClient(new Point(e.X, e.Y)));
        var rowMenu = Menus.Create();
        rowMenu.Item("Play from here", Glyph.Play, (_, _) => PlayRequested?.Invoke());
        rowMenu.Item("Move up", Glyph.ArrowUp, (_, _) => MoveSelected(-1), keys: "Alt+Up");
        rowMenu.Item("Move down", Glyph.ArrowDown, (_, _) => MoveSelected(1), keys: "Alt+Down");
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Item("Remove from playlist", Glyph.Trash, (_, _) => RemoveSelected(), danger: true, keys: "Del");
        rowMenu.Opening += (_, e) => e.Cancel = _list.SelectedIndices.Count == 0;
        _list.ContextMenuStrip = rowMenu;

        _add = new ThemedButton("Add songs…", ButtonKind.Tonal, Glyph.Plus) { FixedHeight = Theme.S(28) };
        _add.Size = _add.GetPreferredSize(Size.Empty);
        _add.Location = Theme.S(7, 228, 0, 0).Location;
        _add.Click += (_, _) => AddSongs();
        Controls.Add(_add);
        var x = _add.Right + Theme.S(6);
        _remove = Icon(Glyph.Trash, "Remove from playlist", 0, 228);
        _up = Icon(Glyph.ArrowUp, "Move up", 0, 228);
        _down = Icon(Glyph.ArrowDown, "Move down", 0, 228);
        foreach (var b in new[] { _remove, _up, _down })
        {
            b.Left = x;
            x += b.Width + Theme.S(2);
        }
        _remove.Click += (_, _) => RemoveSelected();
        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);
        _tips.SetToolTip(_remove, "Remove from playlist (Del)");
        _tips.SetToolTip(_up, "Move up (Alt+Up)");
        _tips.SetToolTip(_down, "Move down (Alt+Down)");
        if (next is not null)
        {
            _next = new ThemedButton("Next", ButtonKind.Secondary, Glyph.Next) { FixedHeight = Theme.S(28), Enabled = false };
            _next.Size = _next.GetPreferredSize(Size.Empty);
            _next.Location = new Point(Theme.S(387) - _next.Width, Theme.S(228));
            _next.Click += (_, _) => next();
            Controls.Add(_next);
            _tips.SetToolTip(_next, "Skip to the next song of the playlist that is playing");
        }

        _picker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            ShowPlaylist(_picker.Selected);
            _settings.LastPlaylist = _current?.Name ?? "";
            SaveOptions();
        };
        _picker.NewRequested += (_, _) => NewPlaylist();
        menu.Opening += (_, _) => { _rename.Enabled = _delete.Enabled = _current is not null; };
        LoadNames(settings.LastPlaylist);

        ThemedButton Icon(Glyph glyph, string name, float x96, float y96)
        {
            var b = new ThemedButton("", ButtonKind.Ghost, glyph) { AccessibleName = name, FixedHeight = Theme.S(30) };
            b.Size = b.GetPreferredSize(Size.Empty);
            b.Location = Theme.S(x96, y96, 0, 0).Location;
            if (y96 > 100) b.Size = Theme.S(28, 28);
            Controls.Add(b);
            return b;
        }
    }

    /// <summary>Tag of each list row.</summary>
    private sealed record RowState(bool Known, bool Playing);

    /// <summary>The playlists were created, renamed or deleted (the main window shows how many there are).</summary>
    public event Action? PlaylistsChanged;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // A hairline above the bottom toolbar.
        using var line = new SolidBrush(Theme.Divider);
        e.Graphics.FillRectangle(line, 0, Theme.S(221), Width, Math.Max(1, Theme.S(1)));
    }

    /// <summary>Play (double-click, Enter, "Play from here", or the main window's Play when this tab is showing).</summary>
    public event Action? PlayRequested;

    /// <summary>A row was selected: the song name (to show its description), or null.</summary>
    public event Action<string?>? SongSelected;

    public Playlist? Current => _current;
    public bool Shuffle => _shuffle.Checked;
    public bool Repeat => _repeat.Checked;

    /// <summary>Where Play starts: the selected row, or the first song.</summary>
    public int StartIndex => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : 0;

    public string? SelectedSong => _current is not null && _list.SelectedIndices.Count > 0 ? _current.Songs[_list.SelectedIndices[0]] : null;

    /// <summary>The library changed (songs added, deleted, renamed): refresh names, players and missing markers.</summary>
    public void LibraryChanged()
    {
        _library = _librarySongs().GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        LoadNames(_current?.Name ?? _settings.LastPlaylist);
    }

    /// <summary>All playlist names, for the Songs tab's "Add to playlist" menu.</summary>
    public IReadOnlyList<string> PlaylistNames => _store.Names();

    /// <summary>Adds songs to a playlist (from the Songs tab), creating it first when <paramref name="playlist"/> is null.</summary>
    public void AddTo(string? playlist, IReadOnlyList<string> songs)
    {
        try
        {
            var target = playlist is null ? AskNewPlaylist() : _store.Load(playlist);
            if (target is null) return;
            _store.Save(target with { Songs = target.Songs.Concat(songs).ToList() });
            LoadNames(target.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not save the playlist: " + ex.Message); }
    }

    /// <summary>Highlights the song now playing when it belongs to the playlist on show; enables Next when it can skip.</summary>
    public void SetNowPlaying(NowPlayingInfo? info, bool canSkip)
    {
        _nowPlaying = info;
        if (_next is not null) _next.Enabled = canSkip;
        Render(keepSelection: true);
    }

    // ------------------------------------------------------------------ playlists

    private void LoadNames(string? select)
    {
        _loading = true;
        try
        {
            var names = _store.Names();
            var chosen = select is not null && names.Contains(select) ? select : names.FirstOrDefault();
            _picker.SetItems(names, chosen);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not read the playlists: " + ex.Message); }
        finally { _loading = false; }
        ShowPlaylist(_picker.Selected);
        PlaylistsChanged?.Invoke();
    }

    private void ShowPlaylist(string? name)
    {
        try { _current = name is null ? null : _store.Load(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not read the playlist: " + ex.Message); _current = null; }
        _picker.SongCount = _current?.Songs.Count;
        Render(keepSelection: false);
    }

    private void Render(bool keepSelection, IReadOnlyCollection<int>? select = null)
    {
        var selected = select ?? (keepSelection ? _list.SelectedIndices.Cast<int>().ToList() : new List<int>());
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_current is not null)
        {
            var playingHere = _nowPlaying is { Playlist: { } p } && p == _current.Name;
            for (var i = 0; i < _current.Songs.Count; i++)
            {
                var song = _current.Songs[i];
                var known = _library.TryGetValue(song, out var entry);
                var playing = playingHere && song == _nowPlaying!.Song;
                var item = new ListViewItem(new[] { (i + 1).ToString(), song, known ? entry!.Players.Trim() : "–" }) { Tag = new RowState(known, playing) };
                if (!known) item.ToolTipText = "Not in the song list - it will be skipped.";
                _list.Items.Add(item);
            }
            foreach (var i in selected.Where(i => i < _list.Items.Count)) _list.Items[i].Selected = true;
            if (selected.Count > 0 && selected.First() < _list.Items.Count) _list.EnsureVisible(selected.First());
        }
        _list.EndUpdate();
        _list.FitColumns();
        _picker.SongCount = _current?.Songs.Count;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var any = _list.SelectedIndices.Count > 0;
        _add.Enabled = _current is not null;
        _remove.Enabled = _up.Enabled = _down.Enabled = any;
    }

    private Playlist? AskNewPlaylist()
    {
        while (true)
        {
            var (ok, name) = InputDialog.Ask(FindForm(), "New playlist", "Name of the new playlist", "", "Create");
            if (!ok) return null;
            try { return _store.Create(name); }
            catch (ArgumentException ex) { Dialogs.Show(this, ex.Message, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    private void NewPlaylist()
    {
        try
        {
            if (AskNewPlaylist() is { } p) LoadNames(p.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not create the playlist: " + ex.Message); }
    }

    private void RenameCurrent()
    {
        if (_current is null) return;
        var (ok, name) = InputDialog.Ask(FindForm(), "Rename playlist", "New name", _current.Name, "Rename");
        if (!ok) return;
        try
        {
            _store.Rename(_current.Name, name);
            LoadNames(name.Trim());
        }
        catch (ArgumentException ex) { Dialogs.Show(this, ex.Message, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not rename the playlist: " + ex.Message); }
    }

    private void DeleteCurrent()
    {
        if (_current is null) return;
        if (!Dialogs.Confirm(this, $"Delete the playlist \u201C{_current.Name}\u201D?", "The songs themselves are not deleted.", "Delete", "Delete playlist"))
            return;
        try
        {
            _store.Delete(_current.Name);
            LoadNames(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not delete the playlist: " + ex.Message); }
    }

    private void SaveOptions()
    {
        _settings.PlaylistShuffle = _shuffle.Checked;
        _settings.PlaylistRepeat = _repeat.Checked;
        try { _settings.SavePlaylistOptions(_dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("Could not save Config.txt: " + ex.Message); }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_store.Dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.Dir) { UseShellExecute = true });
        }
        catch (Exception ex) { _report("Could not open the Playlists folder: " + ex.Message); }
    }

    // ------------------------------------------------------------------ songs

    private void Save(List<string> songs, IReadOnlyCollection<int> select)
    {
        if (_current is null) return;
        try
        {
            _current = _current with { Songs = songs };
            _store.Save(_current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not save the playlist: " + ex.Message); }
        Render(keepSelection: false, select);
    }

    private void AddSongs()
    {
        if (_current is null) return;
        var chosen = AddSongsDialog.Ask(FindForm()!, _current.Name, _librarySongs().Select(s => s.Name).Distinct().ToList());
        if (chosen.Count == 0) return;
        var songs = _current.Songs.ToList();
        var at = songs.Count;
        songs.AddRange(chosen);
        Save(songs, Enumerable.Range(at, chosen.Count).ToList());
    }

    private void RemoveSelected()
    {
        if (_current is null || _list.SelectedIndices.Count == 0) return;
        var remove = _list.SelectedIndices.Cast<int>().ToHashSet();
        var first = remove.Min();
        var songs = _current.Songs.Where((_, i) => !remove.Contains(i)).ToList();
        Save(songs, songs.Count == 0 ? Array.Empty<int>() : new[] { Math.Min(first, songs.Count - 1) });
    }

    /// <summary>Moves the selected rows one place up (-1) or down (+1), keeping them selected.</summary>
    private void MoveSelected(int direction)
    {
        if (_current is null || _list.SelectedIndices.Count == 0) return;
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if ((direction < 0 && selected[0] == 0) || (direction > 0 && selected[^1] == _current.Songs.Count - 1)) return;
        var songs = _current.Songs.ToList();
        foreach (var i in direction < 0 ? selected : Enumerable.Reverse(selected))
            (songs[i], songs[i + direction]) = (songs[i + direction], songs[i]);
        Save(songs, selected.Select(i => i + direction).ToList());
    }

    /// <summary>Drag and drop: the selected rows move to the row under the mouse (or the end).</summary>
    private void DropAt(Point client)
    {
        if (_current is null || _list.SelectedIndices.Count == 0) return;
        var target = _list.GetItemAt(client.X, client.Y)?.Index ?? _current.Songs.Count;
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        var moving = selected.Select(i => _current.Songs[i]).ToList();
        var songs = _current.Songs.Where((_, i) => !selected.Contains(i)).ToList();
        var insertAt = Math.Clamp(target - selected.Count(i => i < target), 0, songs.Count);
        songs.InsertRange(insertAt, moving);
        Save(songs, Enumerable.Range(insertAt, moving.Count).ToList());
    }
}

/// <summary>The playlist chooser: a filled field showing the playlist and its song count; clicking lists the playlists.</summary>
internal sealed class PlaylistPicker : Control
{
    private IReadOnlyList<string> _items = Array.Empty<string>();
    private string? _selected;
    private int? _count;
    private bool _hover;

    public PlaylistPicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        AccessibleName = "Playlist";
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? NewRequested;

    public string? Selected => _selected;

    public int? SongCount
    {
        get => _count;
        set { _count = value; Invalidate(); }
    }

    public void SetItems(IReadOnlyList<string> items, string? selected)
    {
        _items = items;
        Select(selected);
    }

    private void Select(string? name)
    {
        if (_selected == name) { Invalidate(); return; }
        _selected = name;
        AccessibleDescription = name;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenList()
    {
        var menu = Menus.Create();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        foreach (var name in _items)
        {
            var item = menu.Item(name, Glyph.Notes, (_, _) => Select(name));
            item.Checked = name == _selected;
        }
        if (_items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Item("New playlist…", Glyph.Plus, (_, _) => NewRequested?.Invoke(this, EventArgs.Empty));
        menu.MinimumSize = new Size(Width, 0);
        menu.Show(this, new Point(0, Height + Theme.S(2)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left) OpenList();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var i = _selected is null ? -1 : _items.ToList().IndexOf(_selected);
        if (e.KeyCode == Keys.Down && i < _items.Count - 1) Select(_items[i + 1]);
        else if (e.KeyCode == Keys.Up && i > 0) Select(_items[i - 1]);
        else if (e.KeyCode is Keys.Space or Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) OpenList();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, r, Theme.Sf(6), _hover ? Theme.IceHover : Theme.Inset);
        Theme.DrawRound(g, r, Theme.Sf(6), Focused ? Theme.Azure : _hover ? Theme.BorderStrong : Theme.Divider, Focused ? 1.5f : 1f);
        var s = Theme.Sf(14);
        Glyphs.Draw(g, Glyph.Notes, new RectangleF(Theme.Sf(10), (Height - s) / 2, s, s), Theme.Lavender);
        var x = Theme.S(32);
        var chevron = Theme.S(28);
        var name = _selected ?? "No playlists yet";
        var count = _selected is not null && _count is { } c ? (c == 1 ? "1 song" : c + " songs") : "";
        var countW = count.Length > 0 ? Theme.Measure(count, Theme.Small).Width : 0;
        var nameW = Math.Min(Theme.Measure(name, Theme.BodyBold).Width + Theme.S(2), Width - x - chevron - countW - Theme.S(8));
        Theme.Text(g, name, Theme.BodyBold, new Rectangle(x, 0, Math.Max(0, nameW), Height), _selected is null ? Theme.Muted : Theme.Title);
        if (countW > 0) Theme.Text(g, count, Theme.Small, new Rectangle(x + nameW + Theme.S(6), 0, countW + Theme.S(2), Height), Theme.Muted);
        var cs = Theme.Sf(12);
        Glyphs.Draw(g, Glyph.ChevronDown, new RectangleF(Width - Theme.Sf(22), (Height - cs) / 2, cs, cs), Theme.Ink2);
    }
}
