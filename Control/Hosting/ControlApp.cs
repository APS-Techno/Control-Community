using APS.Controls.Theme;
using APS.SystemControl.Accounts;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Forms;

namespace APS.SystemControl.Hosting;

public static class ControlApp
{
    private static Mutex? _instanceMutex;
    private static ControlEdition? _edition;

    // A broken RootConfig.xml is reported by FrmMain with its path and cause; here it only
    // means "no palette overrides", so the login dialog can still render.
    private static string? TryResolveConfigDir()
    {
        try { return new APS.SystemControl.Core.CoreClass.Config().ConfigDir; }
        catch { return null; }
    }

    internal static ControlEdition Edition => _edition ?? ControlEdition.Community;

    public static void Restart()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { /* best-effort */ }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        Application.Restart();
    }

    public static void Run(ControlEdition edition)
    {
        _edition = edition;

        bool createdNew;
        try
        {
            _instanceMutex = new Mutex(true, @"Global\APS_SystemControl", out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists under ANOTHER Windows account (fast user
            // switching on a shared shop-floor PC): its default DACL denies
            // this account. Denied access proves the app is already running.
            createdNew = false;
        }
        if (!createdNew)
        {
            ThemedMessageBox.Show($"{edition.ProductName} is already running.",
                "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "APS", "Control");
        Directory.CreateDirectory(appDir);

        // Install before any UI / threads start, so any failure during init
        // also goes through the formatted log + dialog path.
        var crashLogPath = Path.Combine(appDir, "crash.log");
        var asmVersion = System.Reflection.Assembly.GetEntryAssembly()
            ?.GetName().Version?.ToString() ?? "unknown";
        CrashReporter.Configure(crashLogPath, asmVersion);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashReporter.Report(e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashReporter.Report(ex);
        };

        if (!edition.Startup.AuthorizeProduct()) return;

        // Control is ALWAYS dark. Force it before any window is created so
        // native chrome renders dark, and pin VacuumTheme so every form themes
        // itself regardless of the (unreliable) .NET 9 color-mode APIs — on a
        // light-themed Windows box neither Application.SystemColorMode nor
        // Application.ColorMode reports Dark even after SetColorMode(Dark).
        Application.SetColorMode(SystemColorMode.Dark);
        VacuumTheme.ForceDark = true;
        VacuumTheme.LoadPalette(TryResolveConfigDir());

        APS.SystemControl.Core.UIScaling.InitializeFromRootConfig();

        // Runs after theming so a login dialog renders dark. A null result
        // means the user exited the dialog; exit cleanly.
        var session = edition.Startup.OpenSession();
        if (session is null) return;
        CurrentUser.Sessions = session;

        Application.EnableVisualStyles();

        frmSplash? splash = null;
        var splashThread = new Thread(() =>
        {
            splash = new frmSplash();
            Application.Run(splash);
        });
        splashThread.SetApartmentState(ApartmentState.STA);
        splashThread.IsBackground = true;
        splashThread.Start();

        while (splash == null || !splash.IsHandleCreated)
            Thread.Sleep(20);

        splash.UpdateStatus("Initializing application...", 5);

        var form = new FrmMain(splash.UpdateStatus);

        if (form._disposed)
            splash.UpdateStatus("", 100);

        if (!form._disposed)
            Application.Run(form);
    }
}
