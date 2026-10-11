using GW2MM.Core.Config;

namespace GW2MM.Tests;

public sealed class HotkeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-keys-" + Guid.NewGuid().ToString("N"));
    public HotkeyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Theory]
    [InlineData("F1", HotkeyModifiers.None, 0x70u, "F1")]
    [InlineData("Shift+F1", HotkeyModifiers.Shift, 0x70u, "Shift+F1")]
    [InlineData("shift + f8", HotkeyModifiers.Shift, 0x77u, "Shift+F8")]
    [InlineData("Alt+Ctrl+5", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x35u, "Ctrl+Alt+5")]
    [InlineData("Control+Shift+Win+NumPad3", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x63u, "Ctrl+Shift+Win+NumPad3")]
    [InlineData("Comma", HotkeyModifiers.None, 0xBCu, "Comma")]
    public void ParsesAndFormats(string text, HotkeyModifiers mods, uint vk, string canonical)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.Equal(new Hotkey(mods, vk), h);
        Assert.Equal(canonical, h.ToString());
        Assert.DoesNotContain(",", h.ToString());
        Assert.Equal(h, Hotkey.Parse(h.ToString()));
    }

    [Fact]
    public void NoneAndInvalid()
    {
        Assert.True(Hotkey.TryParse("None", out var n) && n.IsNone);
        Assert.True(Hotkey.TryParse("", out var e) && e.IsNone);
        Assert.Equal("None", Hotkey.None.ToString());
        Assert.False(Hotkey.TryParse("Shift+", out _));
        Assert.False(Hotkey.TryParse("Hyper+F1", out _));
        Assert.False(Hotkey.TryParse("F99", out _));
        Assert.False(Hotkey.IsBindable(0x10)); // Shift on its own
    }

    [Fact]
    public void DefaultsApplyUntilChangedAndBindingsPersist()
    {
        File.WriteAllText(Path.Combine(_dir, "Config.txt"), ";Comment\r\nOctaveDelay,130\r\nName,Bob\r\n");
        var s = AppSettings.Load(_dir);
        Assert.Equal(Hotkey.Parse("Shift+F1"), s.HotkeyFor("Window1", "Shift+F1"));

        s.SaveHotkeys(_dir, new Dictionary<string, Hotkey>
        {
            ["Window1"] = Hotkey.Parse("Ctrl+1"), ["PlaySong"] = Hotkey.None, ["RequestSong"] = Hotkey.Parse("F12"),
        });
        var text = File.ReadAllText(Path.Combine(_dir, "Config.txt"));
        Assert.StartsWith(";Comment\nOctaveDelay,130\nName,Bob\n", text);
        Assert.Contains("Key_Window1,Ctrl+1\n", text);

        var again = AppSettings.Load(_dir);
        Assert.Equal(Hotkey.Parse("Ctrl+1"), again.HotkeyFor("Window1", "Shift+F1"));
        Assert.True(again.HotkeyFor("PlaySong", "F1").IsNone);
        Assert.Equal(Hotkey.Parse("F12"), again.HotkeyFor("RequestSong", "F11"));
        Assert.Equal(Hotkey.Parse("Shift+F2"), again.HotkeyFor("Window2", "Shift+F2"));
        Assert.Equal("Bob", again.Name);

        // Saving other settings (and the client's name) must not disturb the hotkey lines, and vice versa.
        again.Name = "Alice";
        again.SaveWithName(_dir);
        again.SaveDisplaySettings(_dir);
        var third = AppSettings.Load(_dir);
        Assert.Equal("Alice", third.Name);
        Assert.Equal(Hotkey.Parse("Ctrl+1"), third.HotkeyFor("Window1", "Shift+F1"));
        third.SaveHotkeys(_dir, new Dictionary<string, Hotkey> { ["Window1"] = Hotkey.Parse("Shift+F1") });
        Assert.Equal(1, File.ReadAllText(Path.Combine(_dir, "Config.txt")).Split('\n').Count(l => l.StartsWith("Key_Window1,")));
        Assert.Equal("Alice", AppSettings.Load(_dir).Name);
    }

    [Fact]
    public void AnInvalidStoredBindingFallsBackToTheDefault()
    {
        File.WriteAllText(Path.Combine(_dir, "Config.txt"), "Key_Window3,Banana\n");
        Assert.Equal(Hotkey.Parse("Shift+F3"), AppSettings.Load(_dir).HotkeyFor("Window3", "Shift+F3"));
    }

    [Theory]
    [InlineData("Ctrl+F3")]
    [InlineData("Ctrl+F5")]
    [InlineData("F5")]
    public void PlaylistAndSoloDefaultsParse(string text) => Assert.Equal(text, Core.Config.Hotkey.Parse(text).ToString());
}
