using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Redball.UI;

/// <summary>
/// Custom entry point that configures assembly resolution for the bin subfolder
/// before any WPF types are loaded. This allows DLLs to live in a 'bin' subdirectory
/// while the main executable stays in the application root.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point. Registers assembly resolution handlers before
    /// starting the WPF application to ensure DLLs in the 'dll' subfolder are found.
    /// </summary>
    [STAThread]
    public static void Main()
    {
        // Register assembly resolver BEFORE any WPF / dependency types are loaded
        var appBaseDir = AppContext.BaseDirectory;
        var dllDir = Path.Combine(appBaseDir, "dll");

        // Preload critical assemblies in a background task to avoid blocking the UI thread during startup
        _ = System.Threading.Tasks.Task.Run(() => PreloadCriticalAssemblies(dllDir));

        // .NET Core/8 primary mechanism: AssemblyLoadContext.Default.Resolving
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            var candidatePath = Path.Combine(dllDir, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                Debug.WriteLine($"[AssemblyResolver] Loading '{assemblyName.Name}' from dll folder");
                return context.LoadFromAssemblyPath(candidatePath);
            }

            // Fallback: try base directory
            candidatePath = Path.Combine(appBaseDir, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                Debug.WriteLine($"[AssemblyResolver] Loading '{assemblyName.Name}' from base directory");
                return context.LoadFromAssemblyPath(candidatePath);
            }

            Debug.WriteLine($"[AssemblyResolver] Failed to find '{assemblyName.Name}' in any search path");
            return null;
        };

        // Fallback: AppDomain.AssemblyResolve (covers edge cases and reflection loads)
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            var candidatePath = Path.Combine(dllDir, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                Debug.WriteLine($"[AppDomain.AssemblyResolve] Loading '{assemblyName.Name}' from dll folder");
                return Assembly.LoadFrom(candidatePath);
            }

            // Fallback: try base directory
            candidatePath = Path.Combine(appBaseDir, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                Debug.WriteLine($"[AppDomain.AssemblyResolve] Loading '{assemblyName.Name}' from base directory");
                return Assembly.LoadFrom(candidatePath);
            }

            return null;
        };

        // Handle native DLL resolution from the dll subdirectory
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (libraryName, assembly, searchPath) =>
        {
            var nativePath = Path.Combine(dllDir, libraryName);
            if (!nativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                nativePath += ".dll";
            }
            if (File.Exists(nativePath) && NativeLibrary.TryLoad(nativePath, out var handle))
            {
                return handle;
            }
            return IntPtr.Zero;
        });

        // Now launch WPF - all dependency assemblies will resolve via bin/
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>
    /// Preloads critical assemblies that are needed early in startup to avoid
    /// race conditions during JIT compilation.
    /// </summary>
    private static void PreloadCriticalAssemblies(string dllDir)
    {
        string[] criticalAssemblies = new[]
        {
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.DependencyInjection",
        };

        foreach (var assemblyName in criticalAssemblies)
        {
            var dllPath = Path.Combine(dllDir, assemblyName + ".dll");
            if (File.Exists(dllPath))
            {
                try
                {
                    Assembly.LoadFrom(dllPath);
                    Debug.WriteLine($"[Preload] Successfully preloaded '{assemblyName}'");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Preload] Failed to preload '{assemblyName}': {ex.Message}");
                }
            }
            else
            {
                // Try loading by name (might be in base directory or GAC)
                try
                {
                    Assembly.Load(assemblyName);
                    Debug.WriteLine($"[Preload] Assembly '{assemblyName}' loaded by name");
                }
                catch
                {
                    Debug.WriteLine($"[Preload] Assembly '{assemblyName}' not found at '{dllPath}'");
                }
            }
        }
    }
}
