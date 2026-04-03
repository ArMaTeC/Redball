namespace Redball.UI.Services;

/// <summary>
/// Interface for Redball plugins. Plugins can hook into lifecycle events
/// to execute custom automation when keep-awake state changes.
/// </summary>
public interface IRedballPlugin
{
    /// <summary>Display name of the plugin.</summary>
    string Name { get; }

    /// <summary>Short description of what the plugin does.</summary>
    string Description { get; }

    /// <summary>Called when the plugin is first loaded.</summary>
    void OnLoad();

    /// <summary>Called when keep-awake is activated.</summary>
    void OnActivate();

    /// <summary>Called when keep-awake is paused.</summary>
    void OnPause();

    /// <summary>Called when a timed session expires.</summary>
    void OnTimerExpire();

    /// <summary>Called when the plugin is unloaded (app shutdown).</summary>
    void OnUnload();
}
