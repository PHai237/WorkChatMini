namespace WorkChatMini;

public partial class MainWindow
{
    // Legacy ContextMenu and normal gear navigation share the same page.
    // No second Popup, so the existing Settings back button always works.
    private void OpenChatAlertSettings()
    {
        if (_isExiting) return;
        EnsureSettingsFlyout();
        if (_settingsFlyout is null) return;
        ShowSettingsPage("chat-alerts");
        _settingsFlyout.IsOpen = true;
    }

    private void AddChatAlertSettingsRows()
    {
        // Keep per-service preferences independent, and leave this page open
        // while background WebViews apply notification permissions asynchronously.
        foreach (MiniSite site in AlertSites)
        {
            MiniSite selectedSite = site;
            SettingsSwitch(site.ToString(), ChatAlertsKeepAlive(site),
                enabled => { _ = ApplyChatAlertChoiceAsync(selectedSite, enabled); });
        }
        SettingsRule();
        SettingsHint("Alerts remain active while WorkChat is running in the background.");
    }
}
