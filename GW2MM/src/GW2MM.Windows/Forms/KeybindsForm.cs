using GW2MM.Core.Config;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Lets the user change every hotkey: click a box and press the new combination; Backspace/Delete clears it.
/// Saving stores the bindings in Config.txt; the main window re-registers them when this window closes.
/// </summary>
internal sealed class KeybindsForm : Form
{
    private readonly IReadOnlyList<HotkeyAction> _actions;
    private readonly Dictionary<string, Hotkey> _keys;
    private readonly Dictionary<string, TextBox> _boxes = new();
    private readonly Label _problem;
    private readonly Action<IReadOnlyDictionary<string, Hotkey>> _save;

    public KeybindsForm(AppRole role, IReadOnlyList<(HotkeyAction Action, Hotkey Keys)> current, Action<IReadOnlyDictionary<string, Hotkey>> save)
    {
        _save = save;
        _actions = current.Select(c => c.Action).ToList();
        _keys = current.ToDictionary(c => c.Action.Id, c => c.Keys);
        Modern.InitDialog(this, "Keybinds");
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = false;

        var root = Modern.Stack(this);
        root.Controls.Add(Modern.Heading("Keybinds"));
        root.Controls.Add(Modern.Caption(
            "Click a box, then press the new key combination. Backspace or Delete removes a binding. " +
            "These keys work everywhere while GW2MM is running, and other programs (including the game) no longer " +
            "receive them, so prefer combinations you don't otherwise use.", 470));

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Modern.Surface, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        foreach (var action in _actions)
        {
            table.Controls.Add(new Label { Text = action.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 16, 6) });
            var box = new TextBox
            {
                ReadOnly = true, Width = 140, TextAlign = HorizontalAlignment.Center, BackColor = Modern.Surface,
                Cursor = Cursors.Hand, ShortcutsEnabled = false, Margin = new Padding(0, 3, 0, 3),
            };
            box.KeyDown += (_, e) => CaptureKey(action.Id, e);
            box.Enter += (_, _) => box.BackColor = Modern.Stripe;
            box.Leave += (_, _) => box.BackColor = Modern.Surface;
            _boxes[action.Id] = box;
            table.Controls.Add(box);
        }
        root.Controls.Add(table);

        _problem = new Label { AutoSize = true, ForeColor = Color.FromArgb(0xB0, 0x3A, 0x2E), MaximumSize = new Size(470, 0), Margin = new Padding(0, 8, 0, 0) };
        root.Controls.Add(_problem);

        var saveButton = Modern.Primary("Save");
        saveButton.Click += (_, _) => Save();
        var cancel = Modern.Secondary("Cancel");
        cancel.Click += (_, _) => Close();
        var reset = Modern.Secondary("Reset to defaults");
        reset.Click += (_, _) =>
        {
            foreach (var a in _actions) _keys[a.Id] = Hotkey.Parse(a.Default);
            UpdateView();
        };
        var buttons = Modern.ButtonRow(saveButton, cancel, reset);
        root.Controls.Add(buttons);
        CancelButton = cancel;
        UpdateView();
    }

    private void CaptureKey(string id, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.Back or Keys.Delete && e.Modifiers == Keys.None)
        {
            _keys[id] = Hotkey.None;
        }
        else
        {
            var vk = (uint)e.KeyCode;
            if (!Hotkey.IsBindable(vk)) return; // a modifier on its own, Tab, Enter, ...
            var mods = HotkeyModifiers.None;
            if (e.Control) mods |= HotkeyModifiers.Ctrl;
            if (e.Alt) mods |= HotkeyModifiers.Alt;
            if (e.Shift) mods |= HotkeyModifiers.Shift;
            _keys[id] = new Hotkey(mods, vk);
        }
        UpdateView();
    }

    // Keys like F10 and Alt+key arrive as system keys; route them to the focused box too.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is TextBox box && _boxes.ContainsValue(box) && (keyData & Keys.KeyCode) is not (Keys.Tab or Keys.Escape))
        {
            var id = _boxes.First(b => b.Value == box).Key;
            CaptureKey(id, new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateView()
    {
        foreach (var (id, box) in _boxes) box.Text = _keys[id].IsNone ? "—" : _keys[id].ToString();
        var duplicates = _keys.Where(k => !k.Value.IsNone).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => g.Key.ToString()).ToList();
        _problem.Text = duplicates.Count > 0 ? "Used twice: " + string.Join(", ", duplicates) + ". Each key can only do one thing." : "";
    }

    private void Save()
    {
        if (_problem.Text.Length > 0) return;
        _save(_keys);
        DialogResult = DialogResult.OK;
        Close();
    }
}
