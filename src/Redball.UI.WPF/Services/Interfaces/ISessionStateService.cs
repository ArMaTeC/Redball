namespace Redball.UI.Services;

/// <summary>
/// Interface for session state persistence.
/// </summary>
public interface ISessionStateService
{
    bool Save(KeepAwakeService keepAwake, string? path = null);
    bool Restore(KeepAwakeService keepAwake, string? path = null);
}
