using System.Windows.Forms;

namespace MouseBridge;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // A second copy would install a second hook and the two would fight
        // over the cursor, so quietly defer to the instance already running.
        using var single = new Mutex(true, @"Local\MouseBridge.SingleInstance", out var isFirst);
        if (!isFirst) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var verbose = args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase));

        using var app = new TrayApp(verbose);
        app.Run();
    }
}
