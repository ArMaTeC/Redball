# Redball Plugin System

## Overview

Redball supports a plugin architecture that allows third-party developers to extend functionality without modifying core code. Plugins can integrate with external services, provide custom input methods, add UI components, and automate workflows.

## Plugin Types

### 1. Input Plugins (`IInputPlugin`)
Provide alternative input methods to TypeThing.

**Use Cases:**
- Voice-to-text input
- Custom keyboard layouts
- Accessibility input aids
- Hardware device integration

**Key Methods:**
- `SendTextAsync()` - Send text with customizable delays
- `SendKeyAsync()` - Send individual key presses

### 2. Integration Plugins (`IIntegrationPlugin`)
Connect with external services and applications.

**Use Cases:**
- Microsoft Teams status
- Slack huddle detection
- Zoom meeting monitoring
- Custom CRM/ERP integration

**Key Methods:**
- `IsServiceRunningAsync()` - Check if integrated service is active
- `IsInMeetingAsync()` - Detect meeting participation
- `GetCurrentMeetingAsync()` - Get meeting metadata

### 3. Automation Plugins (`IAutomationPlugin`)
Provide scripting and automation capabilities.

**Use Cases:**
- Lua/Python scripting support
- Macro recording/playback
- Workflow automation
- Custom actions

**Key Methods:**
- `ExecuteScriptAsync()` - Run automation scripts
- `RegisterAction()` - Add custom actions to palette

### 4. UI Plugins (`IUIPlugin`)
Add custom UI widgets and overlays.

**Use Cases:**
- Custom mini-widget displays
- Overlay information panels
- Alternative tray interfaces
- Dashboard widgets

**Key Methods:**
- `CreateWidget()` - Create UI component
- `WidgetSize` - Report preferred dimensions

## Getting Started

### Creating Your First Plugin

1. **Create a new C# Class Library project**
```bash
dotnet new classlib -n MyRedballPlugin
cd MyRedballPlugin
dotnet add package Redball.Plugins.Abstractions
```

2. **Implement the plugin interface**
```csharp
using Redball.UI.Plugins;

public class MyPlugin : IRedballPlugin
{
    public string PluginId => "com.example.myplugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Description => "My first Redball plugin";
    public string Author => "Your Name";
    public string MinRedballVersion => "3.0.0";
    public PluginCategory Category => PluginCategory.Other;

    public async Task InitializeAsync(IPluginContext context)
    {
        // Setup resources, load config
    }

    public async Task ActivateAsync()
    {
        // Start plugin functionality
    }

    public async Task DeactivateAsync()
    {
        // Pause plugin functionality
    }

    public async Task ShutdownAsync()
    {
        // Cleanup resources
    }
}
```

3. **Build and deploy**
```bash
dotnet build -c Release
copy bin\Release\net10.0-windows\MyRedballPlugin.dll %LOCALAPPDATA%\Redball\Plugins\
```

### Plugin Context

The `IPluginContext` provides access to Redball services:

```csharp
// Logging
context.Logger.Info("Plugin started");

// Configuration
var setting = context.Config.GetString("MySetting", "default");

// Keep-awake control
context.Services.KeepAwake?.RequestKeepAwake("Meeting started", PluginId);

// Notifications
context.Services.Notifications?.ShowNotification("Title", "Message");

// Scheduling
context.Services.Scheduler?.ScheduleOneTime("task1", DateTime.Now.AddMinutes(5), MyTask);

// Events
context.Events.Subscribe<KeepAwakeStateChangedEventArgs>(PluginId, OnStateChanged);

// Storage
context.Storage.Set("key", data);
var data = context.Storage.Get<MyData>("key");
```

## Plugin Lifecycle

```
InitializeAsync()
    ↓
ActivateAsync() ←→ DeactivateAsync() (can repeat)
    ↓
ShutdownAsync()
```

## Sample Plugins

See `src/Redball.UI.WPF/Plugins/Samples/` for example implementations:

- **LoggingNotificationPlugin** - Logs notifications to file
- **SampleIntegrationPlugin** - Demonstrates external service integration

## Best Practices

1. **Handle errors gracefully** - Don't crash Redball if your plugin fails
2. **Clean up resources** - Always implement `ShutdownAsync()` properly
3. **Respect user settings** - Check config before enabling features
4. **Use async/await** - Don't block the UI thread
5. **Version compatibility** - Specify accurate `MinRedballVersion`
6. **Sandbox awareness** - Plugins run with same permissions as Redball

## Plugin Distribution

### Local Installation
Copy plugin DLL to `%LOCALAPPDATA%\Redball\Plugins\`

### Distribution
- NuGet packages (recommended)
- ZIP files with installation instructions
- Redball Plugin Marketplace (future)

## Security Considerations

- Plugins have same file system access as Redball
- Plugins can monitor system state and user activity
- Only install plugins from trusted sources
- Redball will show warning for unsigned plugins

## API Reference

See XML documentation in source files for detailed API reference:
- `IRedballPlugin` - Base plugin interface
- `IPluginContext` - Context and services
- `IKeepAwakePluginService` - Keep-awake control
- `INotificationPluginService` - Notifications
- `ISchedulerPluginService` - Task scheduling

## Troubleshooting

### Plugin not loading
- Check `MinRedballVersion` compatibility
- Verify DLL is in correct directory
- Check Redball logs for errors

### Plugin causing crashes
- Check `InitializeAsync()` for exceptions
- Ensure proper async/await usage
- Verify resource cleanup in `ShutdownAsync()`

## Contributing

To add core plugin support or new interfaces:
1. Discuss in GitHub Issues first
2. Follow existing patterns in `Plugins/` directory
3. Add sample implementation
4. Update this documentation
