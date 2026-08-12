using System.Text;
using System.Windows;
using DSPlayer.Models;

namespace DSPlayer;

public partial class App : System.Windows.Application
{
    public static LaunchArgs LaunchArguments { get; private set; } = LaunchArgs.Parse(Array.Empty<string>());

    protected override void OnStartup(StartupEventArgs e)
    {
        // Shift_JIS (cp932) for 2ch-style DAT / BBS HTML
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        base.OnStartup(e);

        LaunchArguments = LaunchArgs.Parse(e.Args);

        // Per-monitor DPI (also set in csproj ApplicationHighDpiMode)
        var main = new MainWindow(LaunchArguments);
        MainWindow = main;
        main.Show();
    }
}
