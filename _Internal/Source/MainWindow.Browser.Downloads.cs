using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private void ConfigureBrowserDownloadExperience(CoreWebView2 core)
    {
        try
        {
            core.DefaultDownloadDialogCornerAlignment = CoreWebView2DefaultDownloadDialogCornerAlignment.TopRight;
            core.DefaultDownloadDialogMargin = new System.Drawing.Point(8, 8);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BROWSER", $"Download UI configuration failed | {ex.Message}");
        }
    }

    private void BrowserCore_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        BrowserTabState? tab = FindBrowserTabForCore(sender);
        if (tab is null || tab.Closing)
            return;

        CoreWebView2DownloadOperation operation = e.DownloadOperation;
        tab.ActiveDownloadCount++;
        tab.LastUsedAt = DateTimeOffset.UtcNow;

        // Keep WebView2's native download UI. It is compact, accessible, and already supports
        // progress, cancel, open and open-folder actions without duplicating browser chrome.
        e.Handled = false;
        string fileName = SafeDownloadFileName(e.ResultFilePath);
        AppLogger.Info(
            "DOWNLOAD",
            $"Started | tab={tab.Id} | file={fileName} | host={SafeHost(operation.Uri)} | activeForTab={tab.ActiveDownloadCount}");
        RecordPerformanceEvent("DOWNLOAD", $"start tab={tab.Id}; file={fileName}");

        bool finalized = false;
        void OnStateChanged(object? _, object __)
        {
            if (finalized || operation.State == CoreWebView2DownloadState.InProgress)
                return;
            finalized = true;
            try { operation.StateChanged -= OnStateChanged; } catch { }
            tab.ActiveDownloadCount = Math.Max(0, tab.ActiveDownloadCount - 1);
            tab.LastUsedAt = DateTimeOffset.UtcNow;

            if (operation.State == CoreWebView2DownloadState.Completed)
            {
                AppLogger.Info(
                    "DOWNLOAD",
                    $"Completed | tab={tab.Id} | file={SafeDownloadFileName(operation.ResultFilePath)} | bytes={operation.BytesReceived}");
                RecordPerformanceEvent("DOWNLOAD", $"complete tab={tab.Id}; bytes={operation.BytesReceived}");
            }
            else
            {
                AppLogger.Warn(
                    "DOWNLOAD",
                    $"Interrupted | tab={tab.Id} | file={SafeDownloadFileName(operation.ResultFilePath)} | reason={operation.InterruptReason}");
                RecordPerformanceEvent("DOWNLOAD", $"interrupt tab={tab.Id}; reason={operation.InterruptReason}");
            }
        }
        operation.StateChanged += OnStateChanged;
    }

    private async Task WaitForBrowserDownloadsBeforeDisposeAsync(BrowserTabState tab)
    {
        if (tab.ActiveDownloadCount <= 0)
            return;

        AppLogger.Info("DOWNLOAD", $"Tab disposal delayed for active download | tab={tab.Id} | active={tab.ActiveDownloadCount}");
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddHours(2);
        while (!_isExiting && tab.ActiveDownloadCount > 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(500);

        if (tab.ActiveDownloadCount > 0)
            AppLogger.Warn("DOWNLOAD", $"Tab disposal timeout | tab={tab.Id} | active={tab.ActiveDownloadCount}");
    }

    private static string SafeDownloadFileName(string? path)
    {
        try
        {
            string? name = System.IO.Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? "download" : name;
        }
        catch
        {
            return "download";
        }
    }
}
