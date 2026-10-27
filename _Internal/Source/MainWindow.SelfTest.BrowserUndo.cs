namespace WorkChatMini;

public partial class MainWindow
{
    private void VerifyBrowserUndoUrlSelection()
    {
        const string valid = "https://example.com/reader?chapter=7";
        string? recovered = GetRestorableBrowserUrl(
            "about:blank", "chrome-error://chromewebdata/", valid, _settings.SelectedAdBlocker);
        if (recovered != valid)
            throw new InvalidOperationException("An internal WebView2 page hid the last visit URL.");
        if (GetRestorableBrowserUrl("javascript:alert(1)", "file:///C:/private.txt", null,
                _settings.SelectedAdBlocker) is not null ||
            GetRestorableBrowserUrl("https://getadblock.com/en/update/", null, null,
                _settings.SelectedAdBlocker) is not null)
            throw new InvalidOperationException("Unsafe or nuisance URLs entered recently closed tabs.");
        if (GetRestorableBrowserUrl("https://example.org/", valid, null,
                _settings.SelectedAdBlocker) != "https://example.org/")
            throw new InvalidOperationException("A visited URL was overridden by a stale tab address.");
    }
}
