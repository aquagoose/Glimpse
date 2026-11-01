using Glimpse.API.UI;

namespace Glimpse.API;

/// <summary>
/// The base interface for plugins.
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Gets if the plugin has been initialized.
    /// </summary>
    public bool IsInitialized { get; }

    /// <summary>
    /// Gets if the config has been changed since the last save.
    /// </summary>
    public bool ConfigChanged { get; }

    /// <summary>
    /// Called when the plugin is ready to be initialized.
    /// </summary>
    /// <param name="glimpse">The <see cref="IGlimpse"/> instance that is associated with this plugin.</param>
    public void Initialize(IGlimpse glimpse);

    /// <summary>
    /// Called when the plugin can display any extra settings on the settings page.
    /// </summary>
    /// <param name="ui">The immediate GUI instance.</param>
    public void OnGUI(IImmediateGUI ui) { }

    /// <summary>
    /// Save the config to disk.
    /// </summary>
    public void SaveConfigChanges() { }

    /// <summary>
    /// Cancel any changes to the config and revert.
    /// </summary>
    public void CancelConfigChanges() { }
}