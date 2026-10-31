using GW2MM.Windows;
using GW2MM.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppMain.Run(AppRole.Host, args);
}
