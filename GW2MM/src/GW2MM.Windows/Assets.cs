namespace GW2MM.Windows;

internal static class Assets
{
    public static Image Load(string fileName)
    {
        using var s = typeof(Assets).Assembly.GetManifestResourceStream("GW2MM.Assets." + fileName)
                      ?? throw new FileNotFoundException("Missing embedded image " + fileName);
        using var img = Image.FromStream(s);
        return new Bitmap(img);
    }
}
