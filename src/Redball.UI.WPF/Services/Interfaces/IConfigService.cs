using System.Collections.Generic;

namespace Redball.UI.Services;

/// <summary>
/// Interface for configuration management.
/// </summary>
public interface IConfigService
{
    RedballConfig Config { get; }
    string ConfigPath { get; }
    bool IsDirty { get; set; }

    bool Load(string? path = null);
    bool Save(string? path = null);
    List<string> Validate();
    bool Export(string exportPath);
    bool Import(string importPath);
}
