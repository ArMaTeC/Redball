using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Manages named text templates for TypeThing quick re-typing.
/// Templates are stored as JSON in the templates directory.
/// </summary>
public class TemplateService
{
    private static readonly Lazy<TemplateService> _instance = new(() => new TemplateService());
    public static TemplateService Instance => _instance.Value;

    private readonly string _templatesFile;
    private Dictionary<string, string> _templates = new();

    public event EventHandler? TemplatesChanged;

    private TemplateService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _templatesFile = Path.Combine(dir, "templates.json");
        Load();
        Logger.Verbose("TemplateService", $"Templates file: {_templatesFile}, count: {_templates.Count}");
    }

    public IReadOnlyDictionary<string, string> Templates => _templates;

    public List<string> GetTemplateNames()
    {
        return _templates.Keys.OrderBy(k => k).ToList();
    }

    public string? GetTemplate(string name)
    {
        return _templates.TryGetValue(name, out var text) ? text : null;
    }

    public bool SaveTemplate(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(text)) return false;

        _templates[name] = text;
        Flush();
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info("TemplateService", $"Template saved: {name} ({text.Length} chars)");
        return true;
    }

    public bool DeleteTemplate(string name)
    {
        if (!_templates.Remove(name)) return false;

        Flush();
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info("TemplateService", $"Template deleted: {name}");
        return true;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_templatesFile))
            {
                var json = File.ReadAllText(_templatesFile);
                _templates = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("TemplateService", "Failed to load templates", ex);
            _templates = new();
        }
    }

    private void Flush()
    {
        try
        {
            var json = JsonSerializer.Serialize(_templates, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_templatesFile, json);
        }
        catch (Exception ex)
        {
            Logger.Error("TemplateService", "Failed to save templates", ex);
        }
    }
}
