using System;
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
    /// starting the WPF application to ensure DLLs in the 'bin' subfolder are found.
    /// </summary>
    [STAThread]
    public static void Main()
    {
        // Register assembly resolver BEFORE any WPF / dependency types are loaded
        var appBaseDir = AppContext.BaseDirectory;
        var binDir = Path.Combine(appBaseDir, "bin");

        // .NET Core/8 primary mechanism: AssemblyLoadContext.Default.Resolving
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            // Try to find the assembly in the bin subfolder
            var candidatePath = Path.Combine(binDir, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }
            return null;
        };

        // Fallback: AppDomain.AssemblyResolve (covers edge cases and reflection loads)
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            var candidatePath = Path.Combine(binDir, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return Assembly.LoadFrom(candidatePath);
            }
            return null;
        };

        // Also handle native DLL resolution (e.g. InputInterceptor)
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (libraryName, assembly, searchPath) =>
        {
            var nativePath = Path.Combine(binDir, libraryName);
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
}
