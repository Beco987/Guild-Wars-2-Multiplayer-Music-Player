using GW2MM.Core.Config;
using GW2MM.Core.Library;
using GW2MM.Core.Sessions;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The main window's Playlists tab: pick a playlist (the "Playlist ▾" menu creates, renames and deletes them and
/// sets Shuffle / Repeat), see and reorder its songs, add songs from the library and play it from any song. Playing
/// is delegated to the host or client session through <see cref="PlayRequested"/>.
/// </summary>
internal sealed class PlaylistPanel : UserControl
{
    private readonly PlaylistStore _store;
    private readonly AppSettings _settings;
    private readonly string _dataDir;
    private readonly Func<IReadOnlyList<SongEntry>> _librarySongs;
    private readonly Action<string> _report;
    private readonly ComboBox _picker;
    private readonly ListView _list;
    private readonly Button _add, _remove, _up, _down;
    private readonly Button? _next;
    private readonly ToolStripMenuItem _rename, _delete, _shuffle, _repeat;
    private Dictionary<string, SongEntry> _library = new(StringComparer.Ordinal);
    private Playlist? _current;
    private NowPlayingInfo? _nowPlaying;
    private bool _loading;

    /// <param name="next">The host's Next action (null on a client, which has no Next button).</param>
    public PlaylistPanel(PlaylistStore store, AppSettings settings, string dataDir, Func<IReadOnlyList<SongEntry>> librarySongs,
        Action<string> report, Action? next)
    {
        _store = store;
        _settings = settings;
        _dataDir = dataDir;
        _librarySongs = librarySongs;
        _report = report;
        BackColor = SystemColors.Window;

        _picker = Ui.Add(this, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 3, 4, 190, 21);
        var menuButton = Ui.Button(this, "Playlist ▾", 196, 3, 80, 23, (_, _) => { });
        var menu = new ContextMenuStrip();
        menu.Items.Add("New playlist…", null, (_, _) => NewPlaylist());
        _rename = (ToolStripMenuItem)menu.Items.Add("Rename…", null, (_, _) => RenameCurrent());
        _delete = (ToolStripMenuItem)menu.Items.Add("Delete", null, (_, _) => DeleteCurrent());
        menu.Items.Add(new ToolStripSeparator());
        _shuffle = new ToolStripMenuItem("Shuffle") { CheckOnClick = true, Checked = settings.PlaylistShuffle };
        _repeat = new ToolStripMenuItem("Repeat") { CheckOnClick = true, Checked = settings.PlaylistRepeat };
        _shuffle.CheckedChanged += (_, _) => SaveOptions();
        _repeat.CheckedChanged += (_, _) => SaveOptions();
        menu.Items.Add(_shuffle);
        menu.Items.Add(_repeat);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Playlists folder", null, (_, _) => OpenFolder());
        menuButton.Click += (_, _) => menu.Show(menuButton, new Point(0, menuButton.Height));

        _list = Ui.Add(this, new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false, AllowDrop = true, ShowItemToolTips = true,
        }, 3, 30, 273, 212);
        _list.Columns.Add("#", 30, HorizontalAlignment.Right);
        _list.Columns.Add("Song", 172);
        _list.Columns.Add("Players", 50);
        _list.DoubleClick += (_, _) => PlayRequested?.Invoke();
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
        var rowMenu = new ContextMenuStrip();
        rowMenu.Items.Add("Play from here", null, (_, _) => PlayRequested?.Invoke());
        rowMenu.Items.Add("Move up", null, (_, _) => MoveSelected(-1));
        rowMenu.Items.Add("Move down", null, (_, _) => MoveSelected(1));
        rowMenu.Items.Add("Remove from playlist", null, (_, _) => RemoveSelected());
        rowMenu.Opening += (_, e) => e.Cancel = _list.SelectedIndices.Count == 0;
        _list.ContextMenuStrip = rowMenu;

        _add = Ui.Button(this, "Add songs…", 3, 247, 84, 25, (_, _) => AddSongs());
        _remove = Ui.Button(this, "Remove", 90, 247, 62, 25, (_, _) => RemoveSelected());
        _up = Ui.Button(this, "▲", 155, 247, 28, 25, (_, _) => MoveSelected(-1));
        _down = Ui.Button(this, "▼", 186, 247, 28, 25, (_, _) => MoveSelected(1));
        if (next is not null)
        {
            _next = Ui.Button(this, "Next ►", 217, 247, 59, 25, (_, _) => next());
            _next.Enabled = false;
        }
        var tips = new ToolTip();
        tips.SetToolTip(_up, "Move up (Alt+Up)");
        tips.SetToolTip(_down, "Move down (Alt+Down)");
        if (_next is not null) tips.SetToolTip(_next, "Skip to the next song of the playlist that is playing");
        tips.SetToolTip(menuButton, "New, rename, delete, shuffle, repeat");

        _picker.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            ShowPlaylist(_picker.SelectedItem as string);
            _settings.LastPlaylist = _current?.Name ?? "";
            SaveOptions();
        };
        menu.Opening += (_, _) => { _rename.Enabled = _delete.Enabled = _current is not null; };
        LoadNames(settings.LastPlaylist);
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

    /// <summary>Highlights the song now playing when it belongs to the playlist on show; enables Next during a playlist.</summary>
    public void SetNowPlaying(NowPlayingInfo? info)
    {
        _nowPlaying = info;
        if (_next is not null) _next.Enabled = info?.Playlist is not null;
        Render(keepSelection: true);
    }

    // ------------------------------------------------------------------ playlists

    private void LoadNames(string? select)
    {
        _loading = true;
        try
        {
            _picker.BeginUpdate();
            _picker.Items.Clear();
            foreach (var n in _store.Names()) _picker.Items.Add(n);
            _picker.EndUpdate();
            var index = select is null ? -1 : _picker.Items.IndexOf(select);
            if (index < 0 && _picker.Items.Count > 0) index = 0;
            _picker.SelectedIndex = index;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not read the playlists: " + ex.Message); }
        finally { _loading = false; }
        ShowPlaylist(_picker.SelectedItem as string);
    }

    private void ShowPlaylist(string? name)
    {
        try { _current = name is null ? null : _store.Load(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not read the playlist: " + ex.Message); _current = null; }
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
                var item = new ListViewItem(new[] { playing ? "♪" : (i + 1).ToString(), song, known ? entry!.Players.Trim() : "" });
                if (!known)
                {
                    item.ForeColor = SystemColors.GrayText;
                    item.ToolTipText = "Not in the song list - it will be skipped.";
                }
                if (playing) item.Font = new Font(_list.Font, FontStyle.Bold);
                _list.Items.Add(item);
            }
            foreach (var i in selected.Where(i => i < _list.Items.Count)) _list.Items[i].Selected = true;
            if (selected.Count > 0 && selected.First() < _list.Items.Count) _list.EnsureVisible(selected.First());
        }
        _list.EndUpdate();
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
            var (ok, name) = InputDialog.Ask("New playlist", "Name of the new playlist:", "");
            if (!ok) return null;
            try { return _store.Create(name); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Texts.AppTitle); }
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
        var (ok, name) = InputDialog.Ask("Rename playlist", "New name:", _current.Name);
        if (!ok) return;
        try
        {
            _store.Rename(_current.Name, name);
            LoadNames(name.Trim());
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Texts.AppTitle); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report("Could not rename the playlist: " + ex.Message); }
    }

    private void DeleteCurrent()
    {
        if (_current is null) return;
        if (MessageBox.Show(this, $"Delete the playlist \"{_current.Name}\"? The songs themselves are not deleted.", "Confirm action", MessageBoxButtons.YesNo) != DialogResult.Yes)
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
