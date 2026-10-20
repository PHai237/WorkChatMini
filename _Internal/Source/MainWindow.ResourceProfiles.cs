using System.Windows;
using System.Windows.Controls;

namespace WorkChatMini;

public partial class MainWindow
{
    private ResourcePolicy GetResourcePolicy(MiniSite site)
    {
        if (site == MiniSite.YouTube)
            return ResourcePolicy.KeepAlive;
        if (site is MiniSite.Web or MiniSite.Story)
            return ResourcePolicy.Balanced;

        string value = site switch
        {
            MiniSite.Messenger => _settings.MessengerResourcePolicy,
            MiniSite.Facebook => _settings.FacebookResourcePolicy,
            MiniSite.Discord => _settings.DiscordResourcePolicy,
            MiniSite.Zalo => _settings.ZaloResourcePolicy,
            _ => "Balanced"
        };
        return Enum.TryParse(value, true, out ResourcePolicy parsed) ? parsed : ResourcePolicy.Balanced;
    }

    private void SetResourcePolicy(MiniSite site, ResourcePolicy policy)
    {
        if (site is MiniSite.YouTube or MiniSite.Web or MiniSite.Story)
            return;

        switch (site)
        {
            case MiniSite.Messenger: _settings.MessengerResourcePolicy = policy.ToString(); break;
            case MiniSite.Facebook: _settings.FacebookResourcePolicy = policy.ToString(); break;
            case MiniSite.Discord: _settings.DiscordResourcePolicy = policy.ToString(); break;
            case MiniSite.Zalo: _settings.ZaloResourcePolicy = policy.ToString(); break;
        }
        _settings.Save();
        AppLogger.Info("RESOURCE", $"Resource policy changed | site={site} | policy={policy}");
        UpdateMoreMenuState();
        UpdateResourceHealthText();
    }

    private TimeSpan GetSleepThreshold(MiniSite site)
    {
        ResourcePolicy policy = GetResourcePolicy(site);
        if (policy == ResourcePolicy.KeepAlive)
            return TimeSpan.MaxValue;

        return _memoryPressureLevel switch
        {
            MemoryPressureLevel.Critical => policy == ResourcePolicy.Aggressive ? TimeSpan.FromSeconds(45) : TimeSpan.FromSeconds(90),
            MemoryPressureLevel.Elevated => policy == ResourcePolicy.Aggressive ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(4),
            _ => policy == ResourcePolicy.Aggressive ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(10)
        };
    }

    private TimeSpan GetFacebookHibernateThreshold()
    {
        ResourcePolicy policy = GetResourcePolicy(MiniSite.Facebook);
        if (policy == ResourcePolicy.KeepAlive)
            return TimeSpan.MaxValue;

        return _memoryPressureLevel switch
        {
            MemoryPressureLevel.Critical => policy == ResourcePolicy.Aggressive ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(5),
            MemoryPressureLevel.Elevated => policy == ResourcePolicy.Aggressive ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(12),
            _ => policy == ResourcePolicy.Aggressive ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(30)
        };
    }

    private void ResourcePolicyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string raw ||
            !Enum.TryParse(raw, true, out ResourcePolicy policy))
            return;

        if (_activeSite == MiniSite.YouTube)
        {
            AppLogger.Info("RESOURCE", "Ignored resource policy change for YouTube; Smart KeepAlive is automatic");
            return;
        }
        if (_activeSite == MiniSite.Web)
        {
            AppLogger.Info("RESOURCE", "Ignored resource policy change for Web; browser tabs use adaptive tab policy");
            return;
        }
        if (_activeSite == MiniSite.Story)
        {
            AppLogger.Info("RESOURCE", "Ignored resource policy change for Story; native AI UI has no WebView resource policy");
            return;
        }

        SetResourcePolicy(_activeSite, policy);
    }

    private void UpdateResourcePolicyMenuState()
    {
        if (MoreResourcePolicyMenuItem is null)
            return;

        ResourcePolicy policy = GetResourcePolicy(_activeSite);
        MoreResourcePolicyMenuItem.Header = _activeSite switch
        {
            MiniSite.YouTube => "Policy · Smart",
            MiniSite.Web => "Policy · Adaptive",
            MiniSite.Story => "Policy · Native",
            _ => $"Policy · {PolicyLabel(policy)}"
        };
        MoreResourcePolicyMenuItem.IsEnabled = _activeSite is not MiniSite.YouTube and not MiniSite.Web and not MiniSite.Story;
        ResourceKeepAliveMenuItem.IsChecked = policy == ResourcePolicy.KeepAlive;
        ResourceBalancedMenuItem.IsChecked = policy == ResourcePolicy.Balanced;
        ResourceAggressiveMenuItem.IsChecked = policy == ResourcePolicy.Aggressive;
    }

    private static string PolicyLabel(ResourcePolicy policy) => policy switch
    {
        ResourcePolicy.KeepAlive => "Keep alive",
        ResourcePolicy.Aggressive => "Aggressive",
        _ => "Balanced"
    };
}
