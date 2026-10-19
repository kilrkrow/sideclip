namespace Sideclip.Settings;

/// <summary>
/// The one-time tray notice for a profile that existed before history limits
/// and was migrated to Unlimited. It only informs: it never changes a limit and
/// never removes a clip.
/// </summary>
internal static class CapsUpgradeNotice
{
    public const string Title = "Sideclip history limits";

    // ASCII only; a balloon body is limited to 255 characters.
    public const string Text =
        "Your history is unlimited by default. Nothing was removed. "
        + "To set limits for text and image clips, use the tray menu > Settings...";

    /// <summary>
    /// If the notice is still pending, clears the flag, saves it, and returns
    /// true (the caller then shows the balloon). The flag is saved BEFORE the
    /// balloon is shown so the notice is shown at most once. Returns false for
    /// new profiles, profiles that already saw it, and when saving fails.
    /// </summary>
    public static bool TryConsume(AppSettings settings, Action<AppSettings> save)
    {
        if (!settings.CapsUpgradeNoticePending)
            return false;

        settings.CapsUpgradeNoticePending = false;
        try
        {
            save(settings);
            return true;
        }
        catch
        {
            // Could not record it: stay quiet rather than risk repeating.
            return false;
        }
    }
}