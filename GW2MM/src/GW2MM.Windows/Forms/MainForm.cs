using System.Collections;
using GW2MM.Core.Config;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;
using GW2MM.Windows.Interop;

namespace GW2MM.Windows.Forms;

public enum AppRole { Host, Client }

/// <summary>
/// The main window (same layout as the original GW2MM GUI): song list, New/Delete/Data Editor, Play, description,
/// Controls, chat box and status line; the host also has the Port, Sync and Ping buttons.
/// </summary>
internal sealed class MainForm : Form, IHostView
{
    private readonly AppRole _role;
    private readonly string _dataDir;
    private readonly AppSettings _settings;
    private readonly SongLibrary _library;
    private readonly Win32KeyOutput _keys = new();
    private readonly HostSession? _host;
    private readonly ClientSession? _client;
    private readonly string _clientHostAddress = "";
    private readonly string _clientName = "";
    private readonly HotkeyWindow _hotkeys = new();
    private readonly HashSet<string> _renaming = new();
    private readonly CancellationTokenSource _closing = new();

    private readonly ListView _songs;
    private readonly TextBox _description, _dialog, _input;
    private readonly Button _send;
    private readonly Label _status;
    private readonly Button? _syncButton, _pingButton;
    private PortMappingForm? _portForm;
    private NewSongForm? _newSongForm;
    private string _highlighted = "";
    private bool _exiting;

    public MainForm(AppRole role, string dataDir, AppSettings settings, string clientHostAddress = "", string clientName = "")
    {
        _role = role;
        _dataDir = dataDir;
        _settings = settings;
        _clientHostAddress = clientHostAddress;
        _clientName = clientName;
        _library = new SongLibrary(dataDir, new RecycleBin());

        Ui.InitForm(this, Texts.AppTitle, 400, 620);
        KeyPreview = true;

        Ui.Picture(this, "Title.png", 0, 0, 400, 75);
        Ui.Button(this, "?", 380, 0, 20, 20, (_, _) => new InfoForm().Show(this));
        Ui.Button(this, "⚙", 360, 0, 20, 20, (_, _) => new SettingsForm(_settings, _dataDir, ApplyDisplaySettings).Show(this));

        _songs = Ui.Add(this, new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true,
        }, 10, 75, 290, 305);
        _songs.Columns.Add("Name", 170);
        _songs.Columns.Add("Players", 58);
        _songs.Columns.Add("Date", 58);
        _songs.ItemSelectionChanged += (_, e) => { if (e.IsSelected) SelectSong(e.Item!.Text); };
        _songs.ColumnClick += (_, e) => SortBy(e.Column);

        Ui.Button(this, "↻", 300, 365, 20, 20, (_, _) => ReloadSongs());
        Ui.Picture(this, "Page.png", 310, 85, 80, 80, (_, _) => ShowNewSong());
        Ui.Label(this, "New Song", 310, 165, 80, 15, ContentAlignment.TopCenter);
        Ui.Picture(this, "Salvage Kit.png", 310, 185, 80, 80, (_, _) => DeleteSong());
        Ui.Label(this, "Delete Song", 310, 265, 80, 15, ContentAlignment.TopCenter);
        Ui.Picture(this, "Tool Kit.png", 310, 285, 80, 80, (_, _) => OpenDataEditor());
        Ui.Label(this, "Data Editor", 320, 365, 72, 15);

        Ui.Picture(this, "Fa.png", 10, 390, 80, 80, (_, _) => Play());
        Ui.Label(this, "Play", 10, 470, 80, 15, ContentAlignment.TopCenter);
        _description = Ui.Add(this, new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical }, 100, 390, 200, 80);
        Ui.Label(this, "Song Description", 100, 470, 200, 15, ContentAlignment.TopCenter);
        Ui.Picture(this, "return.png", 310, 390, 80, 80, (_, _) =>
            Ui.ShowText(this, "Controls", _role == AppRole.Host ? Texts.HostControls : Texts.ClientControls, 200));
        Ui.Label(this, "Controls", 310, 470, 80, 15, ContentAlignment.TopCenter);

        _dialog = Ui.Add(this, new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical }, 50, 490, 300, 80);
        _input = Ui.Add(this, new TextBox { ReadOnly = true, MaxLength = 65535 }, 50, 570, 300, 22);
        _send = Ui.Button(this, "Send", 355, 570, 45, 22, (_, _) => SendChat());
        _send.Enabled = false;
        AcceptButton = _send;
        _status = Ui.Label(this, "Not connected...", 0, 596, 400, 20, ContentAlignment.TopCenter);

        if (role == AppRole.Host)
        {
            _host = new HostSession(this, _library, () => _settings.OctaveDelay, PrecisionClock.Instance, _keys, new AudioInputDelayMeter());
            Ui.Button(this, "Port", 355, 490, 45, 20, (_, _) => ShowPortMapping());
            _syncButton = Ui.Button(this, "Sync", 355, 510, 45, 20, async (_, _) => await RunGuarded(_syncButton!, () => _host.SyncAsync(_closing.Token)));
            _pingButton = Ui.Button(this, "Ping", 355, 530, 45, 20, async (_, _) => await RunGuarded(_pingButton!, () => _host.PingAsync(_closing.Token)));
        }
        else
        {
            _client = new ClientSession(this, _library, () => _settings.OctaveDelayMs, PrecisionClock.Instance, _keys, new AudioInputDelayMeter());
            _input.TextChanged += (_, _) => _client.TypingChanged(_input.Text);
        }

        ApplyDisplaySettings();
        RegisterHotkeys();
        ReloadSongs();
    }

    // ------------------------------------------------------------------ start-up / shutdown

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_host is not null)
        {
            try { _host.Start(); }
            catch (System.Net.Sockets.SocketException ex)
            {
                MessageBox.Show(this, $"Could not listen for players on port {Core.Net.PeerConnection.DefaultPort}: {ex.Message}", Texts.AppTitle);
                Close();
            }
        }
        else if (_client is not null)
        {
            await _client.ConnectAsync(_clientHostAddress, _clientName);
        }
    }

    private void RegisterHotkeys()
    {
        var failed = new List<string>();
        void Reg(Keys key, bool shift, Action action)
        {
            if (!_hotkeys.Register(key, shift, action)) failed.Add((shift ? "Shift+" : "") + key);
        }

        Reg(Keys.F4, false, () => _ = ExitWithOctaveResetAsync());
        Reg(Keys.F5, false, () => Rename("Player1"));
        Reg(Keys.F6, false, () => Rename("Player2"));
        Reg(Keys.F7, false, () => Rename("Player3"));
        Reg(Keys.F8, false, () => Rename("Player4"));
        Reg(Keys.F9, false, () => Rename("Player5"));
        if (_host is not null)
        {
            Reg(Keys.F1, false, Play);
            Reg(Keys.F2, false, () => _host.TogglePause());
            Reg(Keys.F3, false, () => _ = _host.Stop());
            Reg(Keys.F6, true, () => Rename("Player6"));
            Reg(Keys.F7, true, () => Rename("Player7"));
            Reg(Keys.F8, true, () => Rename("Player8"));
            Reg(Keys.F10, false, () => _host.NudgeTempo(0.01));
            Reg(Keys.F11, false, () => _host.NudgeTempo(-0.01));
        }
        else
        {
            Reg(Keys.F11, false, Play);
        }
        if (failed.Count > 0)
            AppendDialog("Could not register hotkey(s) " + string.Join(", ", failed) + " (in use by another program).");
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
        // The original host window closed on Escape (GuiEscape); the client's did not.
        if (_role == AppRole.Host && e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        _hotkeys.Dispose();
        _host?.Dispose();
        _client?.Dispose();
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------------ commands

    private void Play()
    {
        if (_host is not null) _ = RunLogged(() => _host.PlayLocalAsync(_highlighted));
        else _client?.RequestPlay(_highlighted);
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
        var text = _input.Text;
        if (text.Length == 0) return;
        if (_host is not null) _host.SendChat(text);
        else _client?.SendChat(text);
        _input.Clear();
        _input.Focus();
    }

    private void SelectSong(string name)
    {
        _highlighted = name;
        _description.Text = _library.Description(name);
    }

    private void ReloadSongs()
    {
        _songs.BeginUpdate();
        _songs.Items.Clear();
        try
        {
            foreach (var s in _library.ListSongs()) _songs.Items.Add(new ListViewItem(new[] { s.Name, s.Players, s.Date }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportProblem("Could not read the song list: " + ex.Message);
        }
        _songs.EndUpdate();
    }

    private int _sortColumn = -1;
    private bool _sortDescending;

    private void SortBy(int column)
    {
        _sortDescending = column == _sortColumn && !_sortDescending;
        _sortColumn = column;
        _songs.ListViewItemSorter = new ItemComparer(column, numeric: column == 1, _sortDescending);
    }

    private sealed class ItemComparer(int column, bool numeric, bool descending) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = ((ListViewItem)x!).SubItems[column].Text;
            var b = ((ListViewItem)y!).SubItems[column].Text;
            var r = numeric && double.TryParse(a, out var na) && double.TryParse(b, out var nb)
                ? na.CompareTo(nb)
                : string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
            return descending ? -r : r;
        }
    }

    private void ShowNewSong()
    {
        _newSongForm ??= new NewSongForm(_library, () => { ReloadSongs(); Activate(); });
        _newSongForm.Show(this);
        _newSongForm.Activate();
    }

    private void DeleteSong()
    {
        if (_highlighted.Length == 0) return;
        var answer = MessageBox.Show(this,
            $"Are you sure you wish to delete the following song: {_highlighted}? (This will delete the csv, midi, and entry in the data file)",
            "Confirm action", MessageBoxButtons.YesNo);
        if (answer != DialogResult.Yes) return;
        try { _library.Delete(_highlighted); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            ReportProblem("Could not delete the song: " + ex.Message);
        }
        ReloadSongs();
    }

    private void OpenDataEditor()
    {
        try { new DataEditorForm(DataFileEditor.Open(_library)).Show(this); }
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

    public void SetStatus(string text) => BeginInvokeSafe(() => _status.Text = text);

    public void AppendDialog(string line) => BeginInvokeSafe(() =>
    {
        _dialog.AppendText(line + Environment.NewLine);
        if (_dialog.TextLength > 200_000) _dialog.Text = _dialog.Text[^100_000..];
    });

    public void SetChatEnabled(bool enabled) => BeginInvokeSafe(() =>
    {
        _send.Enabled = enabled;
        _input.ReadOnly = !enabled;
        if (enabled) _input.Focus(); else _input.Clear();
    });

    public void ShowMessage(string text) => BeginInvokeSafe(() => MessageBox.Show(this, text, Texts.AppTitle));

    public Task ShowMessageAndWaitAsync(string text)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvokeSafe(() =>
        {
            MessageBox.Show(this, text, Texts.AppTitle);
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

    public void ShowMainWindow() => BeginInvokeSafe(() =>
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
    });
}
