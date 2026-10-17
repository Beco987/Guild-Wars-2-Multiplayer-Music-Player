using System.Diagnostics;
using GW2MM.Core.Config;
using GW2MM.Windows.Forms;
using GW2MM.Windows.Interop;

namespace GW2MM.Windows;

/// <summary>Start-up shared by GW2MM-Host.exe and GW2MM-Client.exe.</summary>
public static class AppMain
{
    /// <summary>
    /// Runs the application. The data folder (Data.csv, Csv\, Midi\, Config.txt) is the folder containing the
    /// executable unless "--data &lt;folder&gt;" is given.
    /// </summary>
    public static int Run(AppRole role, string[] args)
    {
        var dataDir = AppContext.BaseDirectory;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--data") dataDir = Path.GetFullPath(args[i + 1]);
        Directory.CreateDirectory(dataDir);
        Directory.SetCurrentDirectory(dataDir);
        AppLog.Init(dataDir);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Fatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Write("Unobserved: " + e.Exception); e.SetObserved(); };

        MainForm? form = null;
        using var instance = SingleInstance.Acquire(role + "|" + dataDir, () =>
        {
            var f = form;
            if (f is not null && f.IsHandleCreated) f.BeginInvoke(f.Close);
        });

        // Leftovers of a previous self-update (the replaced .exe) can be removed once that process has exited.
        if (Environment.ProcessPath is { } exe) _ = Task.Run(() => Core.Updates.SelfUpdater.CleanUp(exe));

        // 1 ms timer resolution for the playback loop's sleeps (the original called timeBeginPeriod(1) too).
        NativeMethods.timeBeginPeriod(1);
        try
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; }
            catch (Exception ex) { AppLog.Write("Could not raise process priority: " + ex.Message); }

            var settings = AppSettings.Load(dataDir);
            // A client starts unconnected; it connects from Settings → Connect to server.
            form = new MainForm(role, dataDir, settings, args);
            Application.Run(form);
            return 0;
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
        }
    }

    private static void Fatal(Exception? ex)
    {
        AppLog.Write("Unhandled: " + ex);
        MessageBox.Show("Unexpected error: " + ex?.Message + Environment.NewLine + "Details were written to GW2MM.log.", Texts.AppTitle);
    }
}
