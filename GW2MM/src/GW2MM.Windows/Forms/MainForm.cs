using System.Collections;
using GW2MM.Core.Config;
using GW2MM.Core.Net;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;
using GW2MM.Windows.Interop;

namespace GW2MM.Windows.Forms;

public enum AppRole { Host, Client }

/// <summary>
/// The main window: the Janthir Wilds banner with Settings and About, a Songs | Playlists switch with the song
/// actions, the song list (or the playlist panel), the transport card (Play, the selected song, Pause, Stop), the
/// chat card (host: Port mapping and Sync; client: Connect / Disconnect) and the status bar.
/// </summary>
internal sealed class MainForm : Form, IHostView
{
    private readonly AppRole _role;
    private readonly string _dataDir;
    private readonly AppSettings _settings;
    private readonly SongLibrary _library;
    // One octave tracker for everything that plays on this computer's game windows (host, client and solo play).
    private readonly OctaveTracker _keys = new(new Win32KeyOutput());
    private readonly HostSession? _host;
    private readonly ClientSession? _client;
    /// <summary>Client only: plays on this computer's game windows while not connected to a host.</summary>
    private readonly SoloSession? _solo;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly HashSet<string> _renaming = new();
    private readonly CancellationTokenSource _closing = new();
    private static readonly HttpClient RouterHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private PortOpener? _portOpener;

    private readonly ThemedToolTip _tips = new();
    private readonly SegmentedControl _tabs;
    private readonly Card _listCard;
    private readonly Panel _songsPage;
    private readonly FieldBox _search;
    private readonly ThemedListView _songs;
    private List<SongEntry> _allSongs = new();
    private readonly PlaylistPanel _playlists;
    private readonly ThemedButton _newSong, _editData, _deleteSong, _reload;
    private readonly RoundButton _play, _pause, _stop;
    private readonly Label _playLabel;
    private readonly SongInfoPanel _songInfo;
    private readonly ChatLog _chat;
    private readonly FieldBox _input;
    private readonly ThemedButton _send;
    private readonly StatusBar _status;
    private readonly ThemedButton? _syncButton;
    private readonly ThemedButton? _connectButton;
    private PortMappingForm? _portForm;
    private NewSongForm? _newSongForm;
    private SettingsForm? _settingsForm;
    private InfoForm? _infoForm;
    private string _highlighted = "";
    private string? _playlistSong;
    private NowPlayingInfo? _nowPlaying;
    private bool _exiting;

    private readonly string[] _args;

    private bool PlaylistsShowing => _tabs.SelectedIndex == 1;

    public MainForm(AppRole role, string dataDir, AppSettings settings, string[] args)
    {
        _args = args;
        _role = role;
        _dataDir = dataDir;
        _settings = settings;
        _library = new SongLibrary(dataDir, new RecycleBin());

        Theme.InitWindow(this, Texts.AppTitle);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = Theme.S(420, 660);
        KeyPreview = true;

        // Banner: each program has its own art ("SERVER" / "CLIENT"), with Settings and About in a frosted capsule.
        var banner = Add(new Banner(Assets.Load(Theme.Art(role == AppRole.Client ? "TitleClient.png" : "Title.png"))), 0, 0, 420, 80);
        banner.SettingsClicked += (_, _) => ShowSettings();
        banner.AboutClicked += (_, _) => ShowInfo();

        // Toolbar: Songs | Playlists, then the song actions.
        _tabs = Add(new SegmentedControl("Songs", "Playlists"), 12, 89, 0, 30);
        _tabs.Width = _tabs.PreferredWidth;
        _reload = Add(new ThemedButton("", ButtonKind.Ghost, Glyph.Reload) { AccessibleName = "Reload the song list" }, 380, 90, 28, 28);
        _reload.Click += (_, _) => ReloadSongs();
        _deleteSong = Add(new ThemedButton("", ButtonKind.Ghost, Glyph.Trash) { AccessibleName = "Delete song" }, 338, 90, 28, 28);
        _deleteSong.Click += (_, _) => DeleteSong();
        _editData = Add(new ThemedButton("", ButtonKind.Ghost, Glyph.EditTable) { AccessibleName = "Data Editor" }, 306, 90, 28, 28);
        _editData.Click += (_, _) => OpenDataEditor();
        _newSong = new ThemedButton("New", ButtonKind.Tonal, Glyph.Plus) { AccessibleName = "New song", FixedHeight = Theme.S(28) };
        _newSong.Size = _newSong.GetPreferredSize(Size.Empty);
        _newSong.Location = new Point(Theme.S(300) - _newSong.Width, Theme.S(90));
        Controls.Add(_newSong);
        _newSong.Click += (_, _) => ShowNewSong();
        _tips.SetToolTip(_newSong, "Add a song (.mid or .mscz)");
        _tips.SetToolTip(_editData, "Data Editor: edit names, players, tempo and descriptions");
        _tips.SetToolTip(_deleteSong, "Delete the selected song (Del)");
        _tips.SetToolTip(_reload, "Reload the song list");

        // The list card holds either the song list (with its search box) or the playlist panel.
        _listCard = Add(new Card(), 12, 126, 396, 266);
        _songsPage = new Panel { BackColor = Theme.Surface, Bounds = Theme.S(1, 1, 394, 264) };
        _listCard.Controls.Add(_songsPage);
        _search = new FieldBox(filled: true) { Glyph = Glyph.Search, ClearButton = true, Bounds = Theme.S(7, 7, 380, 30) };
        _search.Box.PlaceholderText = "Search songs";
        _search.Box.TextChanged += (_, _) => ShowSongs();
        _search.Box.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        _search.Box.KeyDown += (_, e) =>
        {
            // Enter jumps to the list (instead of sending the chat message); Down does too.
            if (e.KeyCode is Keys.Enter or Keys.Down && _songs!.Items.Count > 0)
            {
                if (_songs.SelectedItems.Count == 0) _songs.Items[0].Selected = true;
                _songs.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _songsPage.Controls.Add(_search);
        _songs = new ThemedListView { MultiSelect = false, Bounds = Theme.S(3, 43, 388, 219), FillColumn = 0 };
        _songs.Columns.Add("Name", Theme.S(200));
        _songs.Columns.Add("Players", Theme.S(70), HorizontalAlignment.Center);
        _songs.Columns.Add("Added", Theme.S(100), HorizontalAlignment.Right);
        _songs.Look = item => _nowPlaying is { } np && item.Text == np.Song ? new RowLook(Bold: true, Color: Theme.AzureText, Glyph: Glyph.Equalizer) : default;
        _songs.ItemSelectionChanged += (_, e) => { if (e.IsSelected) SelectSong(e.Item!.Text); };
        _songs.ColumnClick += (_, e) => SortBy(e.Column);
        _songs.DoubleClick += (_, _) => Play();
        _songs.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        _songs.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Play(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSong(); e.Handled = true; }
        };
        _songs.ContextMenuStrip = SongMenu();
        _songsPage.Controls.Add(_songs);
        _songs.FitColumns();

        _playlists = new PlaylistPanel(_library.Playlists, settings, dataDir, () => _library.ListSongs(), ReportProblem,
            () => { if (_host is not null) _host.NextSong(); else _solo?.NextSong(); })
        {
            Bounds = Theme.S(1, 1, 394, 264), Visible = false,
        };
        _listCard.Controls.Add(_playlists);
        _playlists.PlayRequested += Play;
        _playlists.SongSelected += name => { _playlistSong = name; UpdateSongInfo(); };
        _playlists.PlaylistsChanged += () => _tabs.SetCount(1, _playlists.PlaylistNames.Count);
        _tabs.SelectedIndexChanged += (_, _) => ShowTab();

        // Transport: Play (the song, or the playlist on the Playlists tab), what will play, Pause and Stop.
        var transport = Add(new Card(), 12, 400, 396, 72);
        _play = new RoundButton(Glyph.Play, primary: true, "Play") { Bounds = Theme.S(16, 4, 50, 50) };
        _play.Click += (_, _) => Play();
        _playLabel = new Label
        {
            Text = "Play", Font = Theme.SmallBold, ForeColor = Theme.AccentInk, BackColor = Theme.Surface, TextAlign = ContentAlignment.TopCenter,
            Bounds = Theme.S(3, 53, 78, 16), AutoEllipsis = true,
        };
        _songInfo = new SongInfoPanel { Bounds = Theme.S(84, 6, 214, 62) };
        _pause = new RoundButton(Glyph.Pause, primary: false, "Pause") { Bounds = Theme.S(306, 9, 36, 36) };
        _pause.Click += (_, _) => RunHotkey("PauseResume");
        _stop = new RoundButton(Glyph.Stop, primary: false, "Stop") { Bounds = Theme.S(348, 9, 36, 36) };
        _stop.Click += (_, _) => RunHotkey("StopSong");
        transport.Controls.AddRange(new Control[] { _play, _playLabel, _songInfo, _pause, _stop, Caption("Pause", 300, 48), Caption("Stop", 342, 48) });

        // Chat: header with the host tools (or the client's connection button), the log, the input and Send.
        var chat = Add(new Card(), 12, 480, 396, 146);
        var chatTitle = new ChatHeader { Bounds = Theme.S(10, 6, 120, 24) };
        _chat = new ChatLog { Bounds = Theme.S(8, 6, 368, 54) };
        var well = new InsetPanel { Bounds = Theme.S(8, 36, 380, 66) };
        well.Controls.Add(_chat);
        _input = new FieldBox { Bounds = Theme.S(8, 108, 300, 30) };
        _input.Box.MaxLength = 65535;
        _input.Box.ReadOnly = true;
        _input.Box.PlaceholderText = role == AppRole.Host ? "Chat opens when a player joins" : "Connect to a server to chat";
        _send = new ThemedButton("Send", ButtonKind.Primary, Glyph.Send) { Enabled = false, FixedHeight = Theme.S(30), Bounds = Theme.S(314, 108, 74, 30) };
        _send.Click += (_, _) => SendChat();
        AcceptButton = _send;
        chat.Controls.AddRange(new Control[] { chatTitle, well, _input, _send });

        _status = Add(new StatusBar { Text = "Not connected..." }, 0, 634, 420, 26);

        if (role == AppRole.Host)
        {
            _host = new HostSession(this, _library, () => _settings.OctaveDelay, PrecisionClock.Instance, _keys, new AudioInputDelayMeter(),
                offsetMs: () => _settings.OffsetMs)
            {
                ResetOctavesBetweenSongs = () => _settings.ResetOctavesBetweenSongs,
                LiveOctaveTimer = () => _settings.LiveOctaveTimer,
                DefaultSpeed = () => _settings.DefaultTempoPercent / 100.0,
            };
            _syncButton = Chip("Sync", Glyph.Metronome, 388);
            _syncButton.Click += async (_, _) => await RunGuarded(_syncButton!, () => _host.SyncAsync(_closing.Token));
            var port = Chip("Port mapping", Glyph.Sliders, 388 - _syncButton.Width / Theme.Scale - 6);
            port.Click += (_, _) => ShowPortMapping();
            chat.Controls.AddRange(new Control[] { _syncButton, port });
            _tips.SetToolTip(port, "Choose which tracks each player plays");
            _tips.SetToolTip(_syncButton, "Measure each player's input delay");
            _status.RightText = "Host";
        }
        else
        {
            _client = new ClientSession(this, _library, () => _settings.OctaveDelayMs, PrecisionClock.Instance, _keys, new AudioInputDelayMeter());
            _input.Box.TextChanged += (_, _) => _client.TypingChanged(_input.Box.Text);
            _solo = new SoloSession(this, _library, () => _settings.OctaveDelay, () => _settings.OctaveDelayMs, PrecisionClock.Instance, _keys,
                offsetMs: () => _settings.OffsetMs)
            {
                ResetOctavesBetweenSongs = () => _settings.ResetOctavesBetweenSongs,
                LiveOctaveTimer = () => _settings.LiveOctaveTimer,
                DefaultSpeed = () => _settings.DefaultTempoPercent / 100.0,
            };
            _connectButton = Chip("Connect…", Glyph.Plug, 388, ButtonKind.Tonal);
            _connectButton.Click += (_, _) =>
            {
                if (_client.IsConnected) _client.Disconnect();
                else ConnectToServer(this);
                UpdateConnectionUi();
            };
            chat.Controls.Add(_connectButton);
            UpdateConnectionUi();
        }

        ApplyDisplaySettings();
        RegisterHotkeys();
        ReloadSongs();
        _tabs.SetCount(1, _playlists.PlaylistNames.Count);
        UpdateSongInfo();
        // Start in the song list (Ctrl+F jumps to the search box), so the search box shows its placeholder.
        ActiveControl = _songs;

        Label Caption(string text, float x, float y) => new()
        {
            Text = text, Font = Theme.Tiny, ForeColor = Theme.Muted, BackColor = Theme.Surface, TextAlign = ContentAlignment.TopCenter,
            Bounds = Theme.S(x, y, 48, 16),
        };
        ThemedButton Chip(string text, Glyph glyph, float right, ButtonKind kind = ButtonKind.Secondary)
        {
            var b = new ThemedButton(text, kind, glyph) { Font = Theme.SmallBold, FixedHeight = Theme.S(26) };
            b.Size = b.GetPreferredSize(Size.Empty);
            b.Location = new Point(Theme.S(right) - b.Width, Theme.S(5));
            return b;
        }
    }

    private T Add<T>(T control, float x, float y, float w, float h) where T : Control
    {
        control.Bounds = Theme.S(x, y, w, h);
        Controls.Add(control);
        return control;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Card.PaintShadows(this, e.Graphics);
    }

    /// <summary>Shows the Songs or the Playlists side; the song actions only apply to the Songs side.</summary>
    private void ShowTab()
    {
        var playlists = PlaylistsShowing;
        _songsPage.Visible = !playlists;
        _playlists.Visible = playlists;
        _newSong.Visible = _editData.Visible = _deleteSong.Visible = !playlists;
        _playLabel.Text = playlists ? "Play playlist" : "Play";
        UpdateSongInfo();
        UpdateTooltips();
    }

    /// <summary>The transport card's text: what Play will play.</summary>
    private void UpdateSongInfo()
    {
        if (PlaylistsShowing)
        {
            var name = _playlists.Current?.Name;
            var song = _playlistSong;
            _songInfo.Set(name is null ? "Playlist" : "Playlist · " + name,
                song ?? name ?? "No playlist yet",
                song is null ? null : PlayersOf(song),
                song is not null ? _library.Description(song) : name is null ? "Create one with ⋯ → New playlist…" : "Plays from the selected song, or from the top.");
            return;
        }
        if (_highlighted.Length == 0)
        {
            _songInfo.Set("Selected song", "No song selected", null, "Pick a song from the list, then press Play.");
            return;
        }
        _songInfo.Set(_nowPlaying?.Song == _highlighted ? "Now playing" : "Selected song", _highlighted, PlayersOf(_highlighted), _library.Description(_highlighted));
    }

    private string? PlayersOf(string song)
    {
        var players = _allSongs.FirstOrDefault(s => s.Name == song)?.Players.Trim();
        return string.IsNullOrEmpty(players) ? null : players == "1" ? "1 player" : players + " players";
    }

    /// <summary>Tooltips that name the keys currently bound to Play, Pause and Stop.</summary>
    private void UpdateTooltips()
    {
        string Key(string id) => CurrentBindings().FirstOrDefault(b => b.Action.Id == id).Keys is { IsNone: false } k ? $" ({k})" : "";
        var playId = _role == AppRole.Host ? "PlaySong" : "RequestSong";
        _tips.SetToolTip(_play, (PlaylistsShowing ? "Play the playlist" : "Play the selected song") + Key(playId));
        _tips.SetToolTip(_pause, "Pause / resume" + Key("PauseResume"));
        _tips.SetToolTip(_stop, "Stop" + Key("StopSong"));
    }

    /// <summary>Client: the chat card's Connect / Disconnect button and the status bar's right-hand text.</summary>
    private void UpdateConnectionUi()
    {
        if (_client is null || _connectButton is null) return;
        var connected = _client.IsConnected;
        _connectButton.Text = connected ? "Disconnect" : "Connect…";
        _connectButton.Kind = connected ? ButtonKind.Secondary : ButtonKind.Tonal;
        _connectButton.Size = _connectButton.GetPreferredSize(Size.Empty);
        _connectButton.Left = Theme.S(388) - _connectButton.Width;
        _tips.SetToolTip(_connectButton, connected ? "Leave the server" : "Connect to a host");
        _status.RightText = connected ? "Playing as " + _settings.Name : "Solo: plays on this PC's windows";
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings, _dataDir, _host is not null, SettingsSaved, ShowKeybinds,
            _client is null ? null : new SettingsForm.Connection(() => _client.IsConnected, ConnectToServer, () => { _client.Disconnect(); UpdateConnectionUi(); }));
        _settingsForm.Show(this);
    }

    private void ShowInfo()
    {
        if (_infoForm is { IsDisposed: false })
        {
            _infoForm.Activate();
            return;
        }
        _infoForm = new InfoForm(_role);
        _infoForm.Show(this);
    }

    // ------------------------------------------------------------------ start-up / shutdown

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_host is not null)
        {
            try
            {
                _host.Start();
                _status.RightText = $"Port {_host.Port}";
                if (_settings.OpenPortAutomatically) _ = OpenPortAsync();
                _ = CheckFirewallAsync();
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Dialogs.Show(this, $"Could not listen for players on port {Core.Net.PeerConnection.DefaultPort}: {ex.Message}", Texts.AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }
        if (_settings.CheckForUpdates && !IsDisposed)
        {
            var exeName = _role == AppRole.Host ? "GW2MM-Host.exe" : "GW2MM-Client.exe";
            await AppUpdater.CheckOnLaunchAsync(this, exeName, _args, SetStatus);
        }
    }

    /// <summary>What each hotkey action does in this role (ids from <see cref="HotkeyCatalog"/>).</summary>
    private Dictionary<string, Action> HotkeyHandlers()
    {
        var h = new Dictionary<string, Action>
        {
            ["Exit"] = () => _ = ExitWithOctaveResetAsync(),
        };
        for (var n = 1; n <= 8; n++)
        {
            var title = "Player" + n;
            h["Window" + n] = () => Rename(title);
        }
        if (_host is not null)
        {
            h["PlaySong"] = Play;
            h["PauseResume"] = () => _host.TogglePause();
            h["NextSong"] = () => _host.NextSong();
            h["StopSong"] = () => _ = _host.Stop();
            h["TempoUp"] = () => _host.ChangeTempo(1);
            h["TempoDown"] = () => _host.ChangeTempo(-1);
        }
        else
        {
            bool Connected() => _client?.IsConnected == true;
            void SoloOnly(Action action)
            {
                if (Connected()) AppendDialog("That key only works while not connected to a host.");
                else action();
            }
            h["RequestSong"] = Play;
            h["PauseResume"] = () => SoloOnly(() => _solo?.TogglePause());
            h["StopSong"] = () =>
            {
                if (Connected()) _client!.SendChat("stop");   // the chat command that stops the song for everyone
                else if (_solo is not null && (_solo.IsPlaying || _solo.IsPlaylistRunning)) _ = RunLogged(_solo.StopAsync);
            };
            h["NextSong"] = () => _solo?.NextSong();
            h["TempoUp"] = () => SoloOnly(() => _solo?.ChangeTempo(1));
            h["TempoDown"] = () => SoloOnly(() => _solo?.ChangeTempo(-1));
        }
        return h;
    }

    /// <summary>Runs what a hotkey action does (the Pause and Stop buttons).</summary>
    private void RunHotkey(string actionId)
    {
        if (HotkeyHandlers().TryGetValue(actionId, out var action)) action();
    }

    /// <summary>The key combination currently assigned to each action of this role.</summary>
    public IReadOnlyList<(HotkeyAction Action, Hotkey Keys)> CurrentBindings() =>
        HotkeyCatalog.For(_role).Select(a => (a, HotkeyCatalog.KeysFor(_role, _settings, a))).ToList();

    /// <summary>(Re)registers every hotkey from the current settings; keys owned by another program are reported.</summary>
    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var handlers = HotkeyHandlers();
        var failed = new List<string>();
        foreach (var (action, keys) in CurrentBindings())
            if (handlers.TryGetValue(action.Id, out var handler) && !_hotkeys.Register(keys, handler))
                failed.Add(keys.ToString());
        if (failed.Count > 0)
            AppendDialog("Could not register hotkey(s) " + string.Join(", ", failed) + " (in use by another program).");
        UpdateTooltips();
    }

    /// <summary>Opens the Keybinds window. Hotkeys are off while it is open so the keys can be pressed into it.</summary>
    public void ShowKeybinds(IWin32Window? owner = null)
    {
        _hotkeys.UnregisterAll();
        try
        {
            using var dlg = new KeybindsForm(_role, CurrentBindings(), saved => _settings.SaveHotkeys(_dataDir, saved));
            dlg.ShowDialog(owner ?? this);
        }
        finally
        {
            RegisterHotkeys();
        }
    }

    private async Task ExitWithOctaveResetAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            if (_host is not null) await _host.ResetOctavesAsync();
            else if (_client is not null) await _client.ResetOctavesAsync();
        }
        finally { Close(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyData == (Keys.Control | Keys.F))
        {
            _tabs.SelectedIndex = 0;             // Ctrl+F: search the songs
            _search.Box.Focus();
            _search.Box.SelectAll();
            e.Handled = e.SuppressKeyPress = true;
            return;
        }
        // The original host window closed on Escape (GuiEscape); the client's did not.
        if (e.KeyCode == Keys.Escape && _search.Box.Focused && _search.Box.TextLength > 0)
        {
            _search.Box.Clear();                 // Esc in the search box clears it
            e.Handled = e.SuppressKeyPress = true;
            return;
        }
        if (_role == AppRole.Host && e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    /// <summary>Host: asks the router to forward the listening port here (UPnP) and reports how that went in the chat box.</summary>
    private async Task OpenPortAsync()
    {
        if (_host is null || _portOpener is not null) return;
        var opener = _portOpener = new PortOpener(RouterHttp);
        try
        {
            var result = await opener.OpenAsync(_host.Port, _closing.Token);
            AppLog.Write("UPnP: " + result.Message);
            if (result.Details.Length > 0) AppLog.Write("UPnP details: " + result.Details);
            if (!ReferenceEquals(_portOpener, opener)) // turned off in Settings while the router was answering
            {
                await opener.CloseAsync(_closing.Token);
                return;
            }
            AppendDialog(result.Message);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Write("UPnP failed: " + ex);
            AppendDialog("Could not open the port automatically: " + ex.Message);
        }
    }

    /// <summary>
    /// Host: makes sure Windows Firewall lets players in on the current network, offering to add a rule if not.
    /// (UPnP only opens the router; the firewall is checked whether or not UPnP is used.)
    /// </summary>
    private async Task CheckFirewallAsync()
    {
        if (_host is null || Environment.ProcessPath is not { } exe) return;
        var port = _host.Port;
        try
        {
            var (status, detail) = await Task.Run(() => FirewallCheck.Check(exe, port));
            AppLog.Write("Firewall: " + detail);
            if (status is FirewallStatus.Allowed or FirewallStatus.Unknown || IsDisposed) return;
            var answer = Dialogs.Show(this,
                detail + Environment.NewLine + Environment.NewLine +
                "Players on other computers can't connect until the host is allowed through Windows Firewall. " +
                "Allow it now? Windows will ask for administrator permission.",
                Texts.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, heading: "Windows Firewall is blocking players", yes: "Allow", no: "Not now");
            if (answer != DialogResult.Yes)
            {
                AppendDialog("Windows Firewall is blocking players from connecting. Restart the host to be asked again, or allow GW2MM-Host in Windows Firewall yourself.");
                return;
            }
            var added = await Task.Run(() => FirewallCheck.AddAllowRule(exe, port));
            var (after, afterDetail) = await Task.Run(() => FirewallCheck.Check(exe, port));
            AppLog.Write($"Firewall rule added: {added}; now: {afterDetail}");
            AppendDialog(added && after == FirewallStatus.Allowed
                ? "Windows Firewall now lets players connect."
                : "Could not change Windows Firewall (" + afterDetail + ")");
        }
        catch (Exception ex) { AppLog.Write("Firewall check failed: " + ex); }
    }

    /// <summary>Host: removes the router forward this program added (waits briefly; the program is closing).</summary>
    private void ClosePort()
    {
        var opener = _portOpener;
        _portOpener = null;
        if (opener is null || !opener.OwnsMapping) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task.Run(() => opener.CloseAsync(cts.Token)).Wait(TimeSpan.FromSeconds(2.5));
            AppLog.Write("UPnP: port forward removed.");
        }
        catch (Exception ex) { AppLog.Write("UPnP: could not remove the port forward: " + ex.Message); }
    }

    /// <summary>Settings → Connect to server: asks for the name and server IP (the last ones filled in) and connects.</summary>
    private void ConnectToServer(IWin32Window owner)
    {
        if (_client is null || ConnectForm.Ask(owner, _settings.Name, _settings.HostAddress) is not { } target) return;
        _settings.Name = target.Name;
        _settings.HostAddress = target.Address;
        try { _settings.SaveClientSettings(_dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("Could not save Config.txt: " + ex.Message); }
        if (_client.IsConnected) _client.Disconnect();
        _ = _client.ConnectAsync(target.Address, target.Name);
        UpdateConnectionUi();
    }

    private void SettingsSaved()
    {
        ApplyDisplaySettings();
        if (_host is null) return;
        if (_settings.OpenPortAutomatically) _ = OpenPortAsync();
        else if (_portOpener is { } opener)
        {
            _portOpener = null;
            if (opener.OwnsMapping) _ = Task.Run(async () =>
            {
                await opener.CloseAsync(_closing.Token);
                AppendDialog($"Port {_host.Port} is no longer forwarded by GW2MM.");
            });
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        ClosePort();
        _hotkeys.Dispose();
        _tips.Dispose();
        _host?.Dispose();
        _client?.Dispose();
        _solo?.Dispose();
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------------ commands

    /// <summary>Play icon / F1: the selected song, or on the Playlists tab the playlist from the selected song.</summary>
    private void Play()
    {
        if (PlaylistsShowing)
        {
            PlayPlaylist();
            return;
        }
        if (_host is not null) _ = RunLogged(() => _host.PlayLocalAsync(_highlighted));
        else if (_client is { IsConnected: true }) _client.RequestPlay(_highlighted);
        else _solo?.PlaySong(_highlighted);
    }

    private void PlayPlaylist()
    {
        if (_playlists.Current is not { } playlist)
        {
            ReportProblem("Create a playlist first (Playlists → ⋯ → New playlist…).");
            return;
        }
        if (playlist.Songs.Count == 0)
        {
            ReportProblem($"The playlist {playlist.Name} is empty. Add songs with \"Add songs…\".");
            return;
        }
        if (_host is not null)
            _ = RunLogged(() => _host.PlayPlaylistLocalAsync(playlist, _playlists.StartIndex, _playlists.Shuffle, _playlists.Repeat));
        else if (_client is { IsConnected: true })
            _client.RequestPlaylist(playlist, _playlists.StartIndex, _playlists.Shuffle, _playlists.Repeat);
        else if (_solo is not null)
            _ = RunLogged(() => _solo.PlayPlaylistAsync(playlist, _playlists.StartIndex, _playlists.Shuffle, _playlists.Repeat));
    }

    /// <summary>The Songs list's right-click menu: play, add to a playlist (or a new one), edit or delete.</summary>
    private ContextMenuStrip SongMenu()
    {
        var menu = Menus.Create();
        var play = menu.Item("Play", Glyph.Play, (_, _) => Play());
        var addTo = menu.Item("Add to playlist", Glyph.Notes, null);
        Menus.Style(addTo);
        menu.Items.Add(new ToolStripSeparator());
        menu.Item("Edit data…", Glyph.EditTable, (_, _) => OpenDataEditor());
        menu.Item("Delete song…", Glyph.Trash, (_, _) => DeleteSong(), danger: true, keys: "Del");
        menu.Opening += (_, e) =>
        {
            if (_songs.SelectedItems.Count == 0) { e.Cancel = true; return; }
            play.ShortcutKeyDisplayString = CurrentBindings().FirstOrDefault(b => b.Action.Id is "PlaySong" or "RequestSong").Keys is { IsNone: false } k
                ? k.ToString() : null;
            var song = _songs.SelectedItems[0].Text;
            addTo.DropDownItems.Clear();
            foreach (var name in _playlists.PlaylistNames)
                addTo.DropDownItems.Add(Menus.Item(name, Glyph.Notes, (_, _) => _playlists.AddTo(name, new[] { song })));
            if (addTo.DropDownItems.Count > 0) addTo.DropDownItems.Add(new ToolStripSeparator());
            addTo.DropDownItems.Add(Menus.Item("New playlist…", Glyph.Plus, (_, _) => { _playlists.AddTo(null, new[] { song }); _tabs.SelectedIndex = 1; }));
        };
        return menu;
    }

    public void OnNowPlaying(NowPlayingInfo? info)
    {
        // Next works for the host's playlists and a client's solo playlist (a client cannot skip the host's songs).
        var canSkip = info?.Playlist is not null && (_host is not null || _solo?.IsPlaylistRunning == true);
        BeginInvokeSafe(() =>
        {
            _nowPlaying = info;
            _playlists.SetNowPlaying(info, canSkip);
            _songs.Invalidate();
            UpdateSongInfo();
        });
    }

    private void Rename(string title)
    {
        // Like an AutoHotkey hotkey thread, a second press while still waiting for the game window is ignored.
        if (!_renaming.Add(title)) return;
        _ = Task.Run(async () =>
        {
            try { await WindowRenamer.RenameWhenActiveAsync(title, _closing.Token); }
            catch (OperationCanceledException) { }
            finally { BeginInvokeSafe(() => _renaming.Remove(title)); }
        });
    }

    private void SendChat()
    {
        var text = _input.Box.Text;
        if (text.Length == 0) return;
        if (_host is not null) _host.SendChat(text);
        else _client?.SendChat(text);
        _input.Box.Clear();
        _input.Box.Focus();
    }

    private void SelectSong(string name)
    {
        _highlighted = name;
        UpdateSongInfo();
    }

    private void ReloadSongs()
    {
        try { _allSongs = _library.ListSongs(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _allSongs = new List<SongEntry>();
            ReportProblem("Could not read the song list: " + ex.Message);
        }
        ShowSongs();
        try { _playlists.LibraryChanged(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ReportProblem("Could not read the playlists: " + ex.Message); }
    }

    /// <summary>Lists the songs that match the search box (all of them when it is empty), keeping the selection.</summary>
    private void ShowSongs()
    {
        var query = _search.Box.Text;
        _songs.BeginUpdate();
        _songs.Items.Clear();
        foreach (var s in _allSongs.Where(s => s.Matches(query)))
        {
            var item = new ListViewItem(new[] { s.Name, s.Players, s.Date }) { Tag = s };
            _songs.Items.Add(item);
            if (s.Name == _highlighted) item.Selected = true;
        }
        _songs.EndUpdate();
        _songs.FitColumns();
        if (_songs.SelectedItems.Count > 0) _songs.SelectedItems[0].EnsureVisible();
        _tabs.SetCount(0, _songs.Items.Count);
    }

    private int _sortColumn = -1;
    private bool _sortDescending;

    private void SortBy(int column)
    {
        _sortDescending = column == _sortColumn && !_sortDescending;
        _sortColumn = column;
        _songs.SortColumn = column;
        _songs.SortDescending = _sortDescending;
        _songs.ListViewItemSorter = new ItemComparer(column, numeric: column == 1, _sortDescending);
        _songs.Invalidate();
    }

    /// <summary>Name and Players sort as text/numbers; Date sorts by the full timestamp, so songs added on the same day keep their order.</summary>
    private sealed class ItemComparer(int column, bool numeric, bool descending) : IComparer
    {
        private const int DateColumn = 2;

        public int Compare(object? x, object? y)
        {
            var ix = (ListViewItem)x!;
            var iy = (ListViewItem)y!;
            int r;
            if (column == DateColumn && ix.Tag is Core.Library.SongEntry ex && iy.Tag is Core.Library.SongEntry ey)
                r = ex.Added.CompareTo(ey.Added);
            else
            {
                var a = ix.SubItems[column].Text;
                var b = iy.SubItems[column].Text;
                r = numeric && double.TryParse(a, out var na) && double.TryParse(b, out var nb)
                    ? na.CompareTo(nb)
                    : string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
            }
            return descending ? -r : r;
        }
    }

    private void ShowNewSong()
    {
        _newSongForm ??= new NewSongForm(_library, _settings, _dataDir, () => { ReloadSongs(); Activate(); });
        _newSongForm.Show(this);
        _newSongForm.Activate();
    }

    private void DeleteSong()
    {
        if (_highlighted.Length == 0) return;
        if (!Dialogs.Confirm(this, $"Delete \u201C{_highlighted}\u201D?",
                "Its CSV and MIDI files go to the Recycle Bin, and its line in Data.csv and its playlist entries are removed.", "Delete", "Delete song"))
            return;
        try
        {
            _library.Delete(_highlighted);
            _highlighted = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            ReportProblem("Could not delete the song: " + ex.Message);
        }
        ReloadSongs();
        UpdateSongInfo();
    }

    private void OpenDataEditor()
    {
        try { new DataEditorForm(DataFileEditor.Open(_library), ReloadSongs).Show(this); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportProblem("Could not open Data.csv: " + ex.Message);
        }
    }

    private void ShowPortMapping()
    {
        _portForm ??= new PortMappingForm(_host!);
        _portForm.Show(this);
        _portForm.Activate();
        // There is no Ping button any more: opening the window measures the players' pings (skipped if already running).
        var host = _host!;
        _ = Task.Run(async () =>
        {
            try { await host.PingAsync(_closing.Token); }
            catch (OperationCanceledException) { }
        });
    }

    private void ApplyDisplaySettings()
    {
        TopMost = _settings.AlwaysOnTop;
        Opacity = _settings.IsTransparent ? 200 / 255.0 : 1.0;
    }

    private static async Task RunGuarded(Button button, Func<Task> action)
    {
        button.Enabled = false;
        try { await RunLogged(action); }
        finally { button.Enabled = true; }
    }

    private static async Task RunLogged(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("Error: " + ex); }
    }

    // ------------------------------------------------------------------ IHostView (called from any thread)

    private void BeginInvokeSafe(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (!IsHandleCreated) { action(); return; }
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    public void SetStatus(string text) => BeginInvokeSafe(() =>
    {
        _status.Text = text;
        UpdateConnectionUi();
    });

    public void LogEvent(string line) => AppLog.Write("Event: " + line);

    public void AppendDialog(string line) => BeginInvokeSafe(() => _chat.AppendLine(line));

    public void SetChatEnabled(bool enabled) => BeginInvokeSafe(() =>
    {
        if (enabled) _solo?.Cancel(); // connected: the host takes over playback
        _send.Enabled = enabled;
        _input.Box.ReadOnly = !enabled;
        _input.Box.PlaceholderText = enabled ? "Message everyone · \u201Cstop\u201D stops the song"
            : _role == AppRole.Host ? "Chat opens when a player joins" : "Connect to a server to chat";
        if (enabled) _input.Box.Focus(); else _input.Box.Clear();
        UpdateConnectionUi();
    });

    public void ShowMessage(string text) => BeginInvokeSafe(() => Dialogs.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information));

    public Task ShowMessageAndWaitAsync(string text)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvokeSafe(() =>
        {
            // Sync prompts: the player is usually in the game (maybe with this window minimized), so make sure they see it.
            Dialogs.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information, heading: "Sync", urgent: true);
            tcs.TrySetResult();
        });
        return tcs.Task;
    }

    public void ReportProblem(string text)
    {
        AppLog.Write(text);
        AppendDialog("*** " + text);
    }

    public void SetInputDelay(int slot, string value) => BeginInvokeSafe(() =>
    {
        _portForm ??= new PortMappingForm(_host!);
        _portForm.SetInputDelay(slot, value);
    });

    public void SetClientPing(int slot, string value) => BeginInvokeSafe(() =>
    {
        _portForm ??= new PortMappingForm(_host!);
        _portForm.SetClientPing(slot, value);
    });

    public void OnClientDisconnected() => BeginInvokeSafe(() =>
    {
        _portForm ??= new PortMappingForm(_host!);
        _portForm.CommitAndHide();
    });

    public void OnPlayersChanged() => BeginInvokeSafe(() => _portForm?.RefreshPlayers());

    public void ShowMainWindow() => BeginInvokeSafe(() =>
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
    });
}

/// <summary>The transport card's text block: a small label, the song (with its player count) and its description.</summary>
internal sealed class SongInfoPanel : Control
{
    private readonly ThemedToolTip _tip = new();
    private string _eyebrow = "", _title = "", _description = "";
    private string? _players;

    public SongInfoPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    public void Set(string eyebrow, string title, string? players, string description)
    {
        _eyebrow = eyebrow;
        _title = title;
        _players = players;
        _description = description.Trim();
        _tip.SetToolTip(this, _description.Length > 60 ? _description : "");
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var playing = _eyebrow == "Now playing";
        var x = 0;
        if (playing)
        {
            Glyphs.Draw(g, Glyph.Equalizer, new RectangleF(0, Theme.Sf(1), Theme.Sf(11), Theme.Sf(11)), Theme.Azure);
            x = Theme.S(15);
        }
        Theme.Text(g, _eyebrow.ToUpperInvariant(), Theme.Overline, new Rectangle(x, 0, Width - x, Theme.S(13)), playing ? Theme.AzureText : Theme.Muted);

        var chipW = 0;
        if (_players is not null) chipW = Theme.Measure(_players, Theme.SmallBold).Width + Theme.S(28);
        var titleW = Math.Min(Theme.Measure(_title, Theme.SongTitle).Width + Theme.S(2), Width - chipW - Theme.S(6));
        Theme.Text(g, _title, Theme.SongTitle, new Rectangle(0, Theme.S(13), Math.Max(0, titleW), Theme.S(22)), Theme.Title);
        if (_players is not null)
        {
            Theme.Smooth(g);
            var chip = new RectangleF(titleW + Theme.S(6), Theme.Sf(15.5f), chipW, Theme.Sf(18));
            Theme.FillRound(g, chip, chip.Height / 2, Theme.LavenderTint);
            Glyphs.Draw(g, Glyph.People, new RectangleF(chip.X + Theme.Sf(7), chip.Y + Theme.Sf(3), Theme.Sf(12), Theme.Sf(12)), Theme.LavenderText);
            Theme.Text(g, _players, Theme.SmallBold, new Rectangle((int)chip.X + Theme.S(22), (int)chip.Y, chipW - Theme.S(24), (int)chip.Height), Theme.LavenderText);
        }
        TextRenderer.DrawText(g, _description, Theme.Small, new Rectangle(0, Theme.S(36), Width, Height - Theme.S(36)), Theme.Muted,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>"CHAT" with its speech-bubble glyph.</summary>
internal sealed class ChatHeader : Control
{
    public ChatHeader()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var s = Theme.Sf(14);
        Glyphs.Draw(e.Graphics, Glyph.Chat, new RectangleF(0, (Height - s) / 2, s, s), Theme.Muted);
        Theme.Text(e.Graphics, "CHAT", Theme.Overline, new Rectangle(Theme.S(20), 0, Width - Theme.S(20), Height), Theme.Muted);
    }
}

/// <summary>A rounded "inset" well (the chat log sits in one).</summary>
internal sealed class InsetPanel : Panel
{
    public InsetPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Inset;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Theme.PaintParent(this, e.Graphics);
        Theme.Smooth(e.Graphics);
        Theme.FillRound(e.Graphics, new RectangleF(0, 0, Width, Height), Theme.Sf(6), BackColor);
    }
}
