using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WorkChatMini;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _runtimeInitialized;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppRuntime.Configure(e.Args);

        // Acquire the instance lock before touching crash markers/log files. A blocked second
        // instance must never overwrite or delete the running instance's recovery marker.
        string mutexName = AppRuntime.IsSelfTest
            ? @"Local\WorkChatMini.SelfTest.Singleton"
            : @"Local\WorkChatMini.Singleton";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (!AppRuntime.IsSelfTest)
            {
                System.Windows.MessageBox.Show(
                    "WorkChat Mini đang chạy rồi. Hãy dùng Alt + Q hoặc icon dưới system tray để mở lại.",
                    "WorkChat Mini",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            Shutdown(2);
            return;
        }

        AppLogger.Initialize();
        CrashDiagnostics.Initialize();
        _runtimeInitialized = true;

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("UNHANDLED", "Dispatcher exception", args.Exception);
            CrashDiagnostics.WriteFatalReport("DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLogger.Error("UNHANDLED", $"AppDomain exception | terminating={args.IsTerminating}", ex);
                CrashDiagnostics.WriteFatalReport("AppDomain.UnhandledException", ex);
            }
            else
            {
                AppLogger.Error("UNHANDLED", $"AppDomain non-Exception object | terminating={args.IsTerminating}");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("UNHANDLED", "Unobserved task exception", args.Exception);
            CrashDiagnostics.WriteExceptionReport("TaskScheduler.UnobservedTaskException", args.Exception);
        };

        base.OnStartup(e);
        if (CrashDiagnostics.PreviousRunUnclean)
        {
            CrashContextState? previous = CrashDiagnostics.PreviousRunContext;
            AppLogger.Warn("CRASHGUARD", $"Previous unclean run detected | site={previous?.ActiveSite ?? "unknown"} | host={previous?.ActiveHost ?? "unknown"} | action={previous?.LastAction ?? "unknown"}");
        }

        AppLogger.Info("APP", $"Creating MainWindow | selftest={AppRuntime.IsSelfTest} | stress={AppRuntime.IsStressSelfTest}");
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_runtimeInitialized)
        {
            AppLogger.Info("APP", $"Application exiting | code={e.ApplicationExitCode}");
            CrashDiagnostics.MarkCleanExit();
        }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // The process may not own the mutex anymore; safe to ignore during shutdown.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
        }

        base.OnExit(e);
        if (_runtimeInitialized)
            AppLogger.Shutdown();
    }
}
