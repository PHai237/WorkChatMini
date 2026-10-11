using System.Diagnostics;
using System.IO;
using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    private sealed record WarpCliResult(int ExitCode, string Output, string Error);

    private bool _vpnBusy;
    private bool _vpnStatusKnown;
    private bool _vpnInstalled;
    private bool _vpnConnected;
    private string? _warpCliPath;
    private string _vpnStatus = "Checking";

    private void VpnMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (VpnPanel.Visibility == Visibility.Visible)
        {
            VpnPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SessionHealthPanel.Visibility = Visibility.Collapsed;
        VpnPanel.Visibility = Visibility.Visible;
        _ = RefreshVpnStatusAsync(updatePanel: true);
        AppLogger.Info("VPN", "VPN panel opened");
    }

    private void CloseVpnPanelButton_Click(object sender, RoutedEventArgs e)
    {
        VpnPanel.Visibility = Visibility.Collapsed;
        AppLogger.Info("VPN", "VPN panel closed");
    }

    private async void VpnToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnBusy)
            return;

        if (!_vpnInstalled)
        {
            UpdateVpnPanel("Cloudflare WARP is not installed. Install it once, then WorkChat can connect/disconnect it here.");
            return;
        }

        _vpnBusy = true;
        UpdateVpnPanel(_vpnConnected ? "Disconnecting WARP..." : "Connecting WARP...");
        try
        {
            WarpCliResult result = await RunWarpCliAsync(_vpnConnected ? "--accept-tos disconnect" : "--accept-tos connect", TimeSpan.FromSeconds(20));

            // A fresh consumer install may not be registered yet. Create the free consumer
            // registration once, then retry connect. No credentials are stored by WorkChat.
            if (!_vpnConnected && (result.ExitCode != 0 || ContainsRegistrationMissing(result)))
            {
                AppLogger.Info("VPN", "WARP registration missing; creating consumer registration");
                WarpCliResult registration = await RunWarpCliAsync("--accept-tos registration new", TimeSpan.FromSeconds(30));
                AppLogger.Info("VPN", $"WARP registration command completed | exit={registration.ExitCode}");
                result = await RunWarpCliAsync("--accept-tos connect", TimeSpan.FromSeconds(20));
            }

            AppLogger.Info("VPN", $"WARP toggle command completed | action={(_vpnConnected ? "disconnect" : "connect")} | exit={result.ExitCode}");
            if (result.ExitCode != 0)
                _vpnStatus = "WARP needs setup. Open the WARP app once, accept its privacy notice, then retry.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("VPN", "WARP toggle failed", ex);
            _vpnStatus = $"VPN error: {ex.Message}";
        }
        finally
        {
            _vpnBusy = false;
            await RefreshVpnStatusAsync(updatePanel: true);
        }
    }

    private async void VpnInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnBusy)
            return;

        _vpnBusy = true;
        UpdateVpnPanel("Installing Cloudflare WARP with Windows Package Manager... A UAC prompt may appear.");
        AppLogger.Info("VPN", "Cloudflare WARP install requested through winget");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget.exe",
                Arguments = "install --id Cloudflare.Warp -e --accept-package-agreements --accept-source-agreements",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };
            using Process? process = Process.Start(psi);
            if (process is null)
                throw new InvalidOperationException("Windows Package Manager could not be started.");

            await process.WaitForExitAsync();
            AppLogger.Info("VPN", $"winget WARP installer exited | code={process.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLogger.Info("VPN", "WARP installation cancelled at UAC prompt");
            _vpnStatus = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("VPN", "WARP installation failed", ex);
            _vpnStatus = $"Install failed: {ex.Message}";
        }
        finally
        {
            _vpnBusy = false;
            await Task.Delay(700);
            await RefreshVpnStatusAsync(updatePanel: true);
        }
    }

    private void VpnOpenAppButton_Click(object sender, RoutedEventArgs e)
    {
        string? exe = FindWarpGuiPath();
        if (exe is null)
        {
            UpdateVpnPanel("Cloudflare WARP app was not found.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            AppLogger.Info("VPN", $"Opened WARP app | exe={Path.GetFileName(exe)}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("VPN", "Could not open WARP app", ex);
        }
    }

    private async Task RefreshVpnStatusAsync(bool updatePanel)
    {
        if (_vpnBusy)
            return;

        _warpCliPath = FindWarpCliPath();
        _vpnInstalled = _warpCliPath is not null;
        _vpnConnected = false;
        _vpnStatusKnown = true;

        if (!_vpnInstalled)
        {
            _vpnStatus = "Not installed · free Cloudflare WARP system tunnel";
            if (updatePanel || VpnPanel.Visibility == Visibility.Visible)
                UpdateVpnPanel(_vpnStatus);
            UpdateVpnMenuState();
            return;
        }

        try
        {
            WarpCliResult result = await RunWarpCliAsync("--accept-tos status", TimeSpan.FromSeconds(8));
            string combined = (result.Output + "\n" + result.Error).Trim();
            string lower = combined.ToLowerInvariant();
            if (lower.Contains("disconnected") || lower.Contains("not connected"))
            {
                _vpnConnected = false;
                _vpnStatus = "Disconnected · WARP is installed";
            }
            else if (lower.Contains("connected"))
            {
                _vpnConnected = true;
                _vpnStatus = "Connected · system traffic is routed through Cloudflare WARP";
            }
            else if (ContainsRegistrationMissing(result))
            {
                _vpnStatus = "Installed · first-time WARP setup is required";
            }
            else
            {
                _vpnStatus = result.ExitCode == 0 ? "Installed · status unavailable" : "Installed · open WARP once to finish setup";
            }
            AppLogger.Info("VPN", $"WARP status refreshed | installed=true | connected={_vpnConnected} | exit={result.ExitCode}");
        }
        catch (Exception ex)
        {
            _vpnStatus = "Installed · status check failed";
            AppLogger.Warn("VPN", $"WARP status failed | {ex.Message}");
        }

        if (updatePanel || VpnPanel.Visibility == Visibility.Visible)
            UpdateVpnPanel(_vpnStatus);
        UpdateVpnMenuState();
    }

    private void UpdateVpnPanel(string status)
    {
        if (VpnStatusText is null)
            return;

        VpnStatusText.Text = status;
        VpnInstallButton.Visibility = !_vpnInstalled ? Visibility.Visible : Visibility.Collapsed;
        VpnToggleButton.Visibility = _vpnInstalled ? Visibility.Visible : Visibility.Collapsed;
        VpnOpenAppButton.Visibility = _vpnInstalled ? Visibility.Visible : Visibility.Collapsed;
        VpnToggleButton.IsEnabled = !_vpnBusy;
        VpnInstallButton.IsEnabled = !_vpnBusy;
        VpnToggleButtonText.Text = _vpnConnected ? "Disconnect" : "Connect";
        UpdateVpnMenuState();
    }

    private void UpdateVpnMenuState()
    {
        if (MoreVpnMenuItem is null)
            return;
        MoreVpnMenuItem.Header = !_vpnStatusKnown
            ? "VPN"
            : !_vpnInstalled
                ? "VPN · install WARP"
                : _vpnConnected ? "VPN · On" : "VPN · Off";
        MoreVpnMenuItem.IsChecked = _vpnStatusKnown && _vpnConnected;
    }

    private async Task<WarpCliResult> RunWarpCliAsync(string arguments, TimeSpan timeout)
    {
        string? cli = _warpCliPath ?? FindWarpCliPath();
        if (cli is null)
            return new WarpCliResult(-1, string.Empty, "warp-cli.exe not found");

        var psi = new ProcessStartInfo
        {
            FileName = cli,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(psi);
        if (process is null)
            return new WarpCliResult(-1, string.Empty, "Could not start warp-cli");

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        await process.WaitForExitAsync(cts.Token);
        return new WarpCliResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static bool ContainsRegistrationMissing(WarpCliResult result)
    {
        string text = (result.Output + " " + result.Error).ToLowerInvariant();
        return text.Contains("registration") &&
               (text.Contains("missing") || text.Contains("not registered") || text.Contains("does not exist"));
    }

    private static string? FindWarpCliPath()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        {
            Path.Combine(programFiles, "Cloudflare", "Cloudflare WARP", "warp-cli.exe"),
            Path.Combine(programFiles, "Cloudflare", "Cloudflare One", "warp-cli.exe"),
            Path.Combine(programFiles, "Cloudflare", "warp-cli.exe")
        };
        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        try
        {
            string cloudflareRoot = Path.Combine(programFiles, "Cloudflare");
            if (Directory.Exists(cloudflareRoot))
                return Directory.EnumerateFiles(cloudflareRoot, "warp-cli.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { }
        return null;
    }

    private static string? FindWarpGuiPath()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        {
            Path.Combine(programFiles, "Cloudflare", "Cloudflare WARP", "Cloudflare WARP.exe"),
            Path.Combine(programFiles, "Cloudflare", "Cloudflare One", "Cloudflare One.exe"),
            Path.Combine(programFiles, "Cloudflare", "Cloudflare WARP", "Cloudflare One.exe")
        };
        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        try
        {
            string cloudflareRoot = Path.Combine(programFiles, "Cloudflare");
            if (Directory.Exists(cloudflareRoot))
                return Directory.EnumerateFiles(cloudflareRoot, "*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => Path.GetFileName(path).Contains("Cloudflare", StringComparison.OrdinalIgnoreCase));
        }
        catch { }
        return null;
    }
}
