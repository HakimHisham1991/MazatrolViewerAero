using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using MazatrolViewerAero.Decoding;
using MazatrolViewerAero.Settings;

namespace MazatrolViewerAero;

public partial class App : Application
{
    private const int FeatureLocalMachineLockdown = 8;
    private const int SetFeatureOnProcess = 0x00000002;

    [DllImport("urlmon.dll")]
    private static extern int CoInternetSetFeatureEnabled(int featureEntry, int flags, bool enable);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            CoInternetSetFeatureEnabled(FeatureLocalMachineLockdown, SetFeatureOnProcess, false);
        }
        catch (Exception)
        {
            // The page still opens when this Internet Explorer feature cannot be changed.
        }
        if (e.Args.Length >= 3 && e.Args[0] == "--render-dir")
        {
            var options = new ViewOptions(false, true, false, false, 0, 0, 0, "MACHINING_(VRX)", 1);
            Directory.CreateDirectory(e.Args[2]);
            int failed = 0;
            foreach (var file in Directory.EnumerateFiles(e.Args[1], "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(e.Args[2], Path.GetFileName(file) + ".html");
                try
                {
                    var document = MazDocument.TryOpen(file, out var failure);
                    if (document == null)
                    {
                        failed++;
                        File.WriteAllText(dest, failure.ToString());
                    }
                    else
                    {
                        File.WriteAllText(dest, document.ToHtml(options));
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    File.WriteAllText(dest, ex.GetType().Name);
                }
            }

            Shutdown(failed);
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--render")
        {
            var options = new ViewOptions(false, true, false, false, 0, 0, 0, "MACHINING_(VRX)", 1);
            try
            {
                var document = MazDocument.TryOpen(e.Args[1], out var failure);
                File.WriteAllText(e.Args[2], document == null ? failure.ToString() : document.ToHtml(options));
                Shutdown(document == null ? 1 : 0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(e.Args[2], ex.GetType().Name);
                Shutdown(1);
            }

            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var settings = ViewerSettings.Load();
        Directory.CreateDirectory(settings.DataPath);
        var window = new MainWindow(settings);
        MainWindow = window;
        if (e.Args.Length == 1 && File.Exists(e.Args[0]))
        {
            window.OpenPath(e.Args[0]);
        }

        window.Show();
    }
}
