using System;
using System.Windows;
using Redball.UI.Views;
using Redball.UI.Services;
using System.Threading;

namespace TestApp;

public class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        
        // Load resources so it looks right
        var themeUri = new Uri("pack://application:,,,/Redball.UI.WPF;component/Themes/DarkTheme.xaml", UriKind.Absolute);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeUri });

        var window = new UpdateProgressWindow();
        window.Show();

        // Simulate some progress
        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        long bytes = 0;
        long total = 50 * 1024 * 1024; // 50 MB
        var start = DateTime.Now;

        timer.Tick += (s, e) => {
            bytes += 1024 * 512; // 0.5 MB per tick
            if (bytes > total) bytes = total;
            
            var elapsed = DateTime.Now - start;
            var speed = elapsed.TotalSeconds > 0 ? bytes / elapsed.TotalSeconds : 0;
            
            window.UpdateProgress(new UpdateDownloadProgress {
                Percentage = (int)(bytes * 100 / total),
                BytesReceived = bytes,
                TotalBytes = total,
                BytesPerSecond = speed,
                StatusText = "Previewing new UI..."
            });
            
            if (bytes >= total) timer.Stop();
        };
        timer.Start();

        app.Run(window);
    }
}
