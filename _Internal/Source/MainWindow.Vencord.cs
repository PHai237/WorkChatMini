using Microsoft.Web.WebView2.Core;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace WorkChatMini;

public partial class MainWindow
{
    private bool _vencordBusy;
    private string _vencordStatus = "Not installed";

    // All WorkChat WebViews currently share one WebView2 profile. The official
    // Vencord extension injects only on discord.com (validated at import), so
    // no separate Discord tab, browser process or replacement login is needed.
    private CoreWebView2Profile? VencordProfile =>
        DiscordWebView.CoreWebView2?.Profile ?? MessengerWebView.CoreWebView2?.Profile;

    private async Task<CoreWebView2BrowserExtension?> FindVencordAsync(CoreWebView2Profile profile)
    {
        IReadOnlyList<CoreWebView2BrowserExtension> extensions =
            await profile.GetBrowserExtensionsAsync();
        return extensions.FirstOrDefault(extension =>
            string.Equals(extension.Name, "Vencord Web", StringComparison.OrdinalIgnoreCase));
    }

    // Fail open to ORIGINAL Discord. Neither a missing extension nor a WebView2
    // extension API error should prevent the Discord web app from navigating.
    private async Task PrepareVencordForDiscordAsync()
    {
        if (AppRuntime.IsSelfTest) return;
        CoreWebView2Profile? profile = VencordProfile;
        if (profile is null) return;
        try
        {
            CoreWebView2BrowserExtension? extension = await FindVencordAsync(profile);
            if (extension is null && _settings.VencordWebEnabled)
            {
                string? cached = VencordWebPackage.GetCachedPackage(_settings.VencordWebPackageSha256);
                if (cached is not null)
                    extension = await profile.AddBrowserExtensionAsync(cached);
            }
            if (extension is not null && extension.IsEnabled != _settings.VencordWebEnabled)
                await extension.EnableAsync(_settings.VencordWebEnabled);
            _vencordStatus = extension is null ? "Not installed" :
                extension.IsEnabled ? "Enabled in Discord" : "Disabled (original Discord)";
            AppLogger.Info("VENCORD", $"Discord extension ready | installed={extension is not null} | enabled={extension?.IsEnabled == true}");
        }
        catch (Exception ex)
        {
            _vencordStatus = "Unavailable — original Discord still works";
            AppLogger.Warn("VENCORD", $"Extension setup skipped: {ex.Message}");
        }
    }

    private void OpenVencordSettings()
    {
        if (_isExiting) return;
        EnsureSettingsFlyout();
        ShowSettingsPage("vencord");
        UpdateSettingsFlyoutSize();
        if (_settingsFlyout is not null) _settingsFlyout.IsOpen = true;
    }

    private void RefreshVencordSettings()
    {
        if (_settingsPage == "vencord" && _settingsFlyout is not null)
            ShowSettingsPage("vencord");
    }

    private bool CanChangeVencord()
    {
        if (_vencordBusy || _isExiting) return false;
        if (_discordCallActive)
        {
            WpfMessageBox.Show(this,
                "End your Discord call before enabling, disabling or updating Vencord.",
                "WorkChat · Protect Discord call", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (VencordProfile is null)
        {
            WpfMessageBox.Show(this, "WebView2 is starting. Try again when Messenger has loaded.",
                "WorkChat · Vencord", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private async Task ToggleVencordAsync(bool enabled)
    {
        if (!CanChangeVencord()) return;
        _vencordBusy = true;
        _vencordStatus = enabled ? "Enabling..." : "Disabling...";
        RefreshVencordSettings();
        try
        {
            CoreWebView2Profile profile = VencordProfile!;
            CoreWebView2BrowserExtension? extension = await FindVencordAsync(profile);
            if (extension is null && enabled)
            {
                string? cached = VencordWebPackage.GetCachedPackage(_settings.VencordWebPackageSha256);
                if (cached is not null) extension = await profile.AddBrowserExtensionAsync(cached);
            }
            if (extension is null)
            {
                _vencordStatus = "Not installed — choose Install Vencord Web";
                return;
            }
            if (extension.IsEnabled != enabled) await extension.EnableAsync(enabled);
            _settings.VencordWebEnabled = enabled;
            _settings.Save();
            _vencordStatus = enabled ? "Enabled in Discord" : "Disabled (original Discord)";
            ReloadDiscordAfterVencordChange();
        }
        catch (Exception ex)
        {
            _vencordStatus = "Could not change Vencord";
            AppLogger.Error("VENCORD", "Toggle failed", ex);
            WpfMessageBox.Show(this, ex.Message, "WorkChat · Vencord", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _vencordBusy = false;
            RefreshVencordSettings();
        }
    }

    private void OpenOfficialVencordDownloads()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://github.com/Vendicated/Vencord/releases/tag/devbuild")
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("VENCORD", $"Could not open official download page: {ex.Message}");
            WpfMessageBox.Show(this,
                "Open https://github.com/Vendicated/Vencord/releases/tag/devbuild in your browser.",
                "WorkChat · Vencord", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task InstallVencordAsync(bool useOfficialDownload)
    {
        if (!CanChangeVencord()) return;
        string? localZip = null;
        if (!useOfficialDownload)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select Vencord Web extension-chrome.zip from the official GitHub release",
                Filter = "Vencord extension ZIP (*.zip)|*.zip", CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            if (WpfMessageBox.Show(this,
                "Only install extension-chrome.zip downloaded from the official Vendicated/Vencord " +
                "GitHub release. A browser extension can access your Discord session. " +
                "Continue with this local ZIP?",
                "WorkChat · Verify Vencord source", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            localZip = dialog.FileName;
        }
        _vencordBusy = true;
        _vencordStatus = useOfficialDownload ? "Downloading verified Vencord..." : "Validating ZIP...";
        RefreshVencordSettings();
        try
        {
            // ZIP decoding and hashing happen off the UI thread; installation
            // into WebView2's profile happens back on the WPF dispatcher.
            (string folder, string sha256) = useOfficialDownload
                ? await VencordWebPackage.DownloadPinnedAsync()
                : await Task.Run(() => VencordWebPackage.InstallLocalZip(localZip!));
            if (_isExiting) return;
            CoreWebView2Profile profile = VencordProfile ??
                throw new InvalidOperationException("WebView2 profile is not available.");
            CoreWebView2BrowserExtension? previous = await FindVencordAsync(profile);
            CoreWebView2BrowserExtension current = await profile.AddBrowserExtensionAsync(folder);
            await current.EnableAsync(true);
            if (previous is not null && previous.Id != current.Id)
            {
                try { await previous.RemoveAsync(); }
                catch
                {
                    await current.RemoveAsync();
                    throw new InvalidOperationException("Unable to replace the previous Vencord extension.");
                }
            }
            _settings.VencordWebPackageSha256 = sha256;
            _settings.VencordWebEnabled = true;
            _settings.Save();
            _vencordStatus = "Enabled in Discord";
            AppLogger.Info("VENCORD", $"Installed extension into existing WebView2 profile | id={current.Id}");
            ReloadDiscordAfterVencordChange();
        }
        catch (Exception ex)
        {
            _vencordStatus = "Installation failed — existing Discord remains available";
            AppLogger.Error("VENCORD", "Install failed", ex);
            WpfMessageBox.Show(this, ex.Message, "WorkChat · Vencord", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _vencordBusy = false;
            RefreshVencordSettings();
        }
    }

    private Task VerifyVencordSettingsSelfTest()
    {
        // Pure test: no network, installation, Discord navigation or account data.
        const string valid = """
            {"manifest_version":3,"name":"Vencord Web","content_scripts":[
              {"matches":["*://*.discord.com/*"],"js":["content.js"]},
              {"matches":["*://*.discord.com/*"],"js":["dist/Vencord.js"]}]}
            """;
        string broad = valid.Replace("*://*.discord.com/*", "<all_urls>", StringComparison.Ordinal);
        string wrongName = valid.Replace("Vencord Web", "Not Vencord", StringComparison.Ordinal);
        if (!VencordWebPackage.IsVencordManifest(valid) ||
            VencordWebPackage.IsVencordManifest(broad) ||
            VencordWebPackage.IsVencordManifest(wrongName) ||
            VencordWebPackage.GetCachedPackage("../../etc") is not null)
            throw new InvalidOperationException("Vencord ZIP manifest/origin validation regressed.");
        EnsureSettingsFlyout();
        ShowSettingsPage("tools");
        if (_settingsFlyoutRows is null || !_settingsFlyoutRows.Children
                .OfType<System.Windows.Controls.Button>().Any(button =>
                    System.Windows.Automation.AutomationProperties.GetName(button) == "Discord + Vencord"))
            throw new InvalidOperationException("Tools must expose the existing Discord + Vencord integration.");
        ShowSettingsPage("vencord");
        if (_settingsFlyoutHeading?.Text != "Discord + Vencord" ||
            _settingsFlyoutRows.Children.OfType<System.Windows.Controls.Button>().Count() != 5 ||
            ParentSettingsPage("vencord") != "tools")
            throw new InvalidOperationException("Vencord Settings actions/back navigation regressed.");
        ShowSettingsPage("root");
        return Task.CompletedTask;
    }

    private void ReloadDiscordAfterVencordChange()
    {
        if (_discordCallActive || !_discordInitialized) return;
        try { DiscordWebView.CoreWebView2?.Reload(); }
        catch (Exception ex) { AppLogger.Warn("VENCORD", $"Reload deferred: {ex.Message}"); }
    }
}
