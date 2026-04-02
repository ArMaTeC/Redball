using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Enables communication between multiple instances of the application.
/// Uses named pipes to signal the primary instance when a second instance is launched.
/// </summary>
public class SingleInstanceMessenger : IDisposable
{
    private const string PipeName = "Redball_SingleInstance_Pipe";
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;
    private readonly Action _onShowWindowRequested;

    /// <summary>
    /// Initializes the messenger with a callback to invoke when a second instance requests the window to be shown.
    /// </summary>
    public SingleInstanceMessenger(Action onShowWindowRequested)
    {
        _onShowWindowRequested = onShowWindowRequested ?? throw new ArgumentNullException(nameof(onShowWindowRequested));
    }

    /// <summary>
    /// Starts listening for messages from other instances.
    /// Call this from the primary (first) instance.
    /// </summary>
    public void StartListening()
    {
        if (_disposed) return;

        _cancellationTokenSource = new CancellationTokenSource();
        Task.Run(() => ListenForMessagesAsync(_cancellationTokenSource.Token));
        Logger.Debug("SingleInstanceMessenger", "Started listening for show-window requests");
    }

    /// <summary>
    /// Sends a message to the primary instance to show its window.
    /// Call this from a secondary (subsequent) instance before exiting.
    /// Returns true if the message was sent successfully.
    /// </summary>
    public static bool TryShowWindow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000); // 1 second timeout

            using var writer = new StreamWriter(client);
            writer.WriteLine("SHOW_WINDOW");
            writer.Flush();

            Logger.Debug("SingleInstanceMessenger", "Sent show-window request to primary instance");
            return true;
        }
        catch (TimeoutException)
        {
            Logger.Warning("SingleInstanceMessenger", "Timeout connecting to primary instance");
            return false;
        }
        catch (IOException ex)
        {
            Logger.Warning("SingleInstanceMessenger", $"Failed to connect to primary instance: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("SingleInstanceMessenger", "Unexpected error sending show-window request", ex);
            return false;
        }
    }

    private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                _pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                Logger.Debug("SingleInstanceMessenger", "Named pipe server waiting for connection...");
                await _pipeServer.WaitForConnectionAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested) break;

                Logger.Debug("SingleInstanceMessenger", "Client connected to pipe");

                using var reader = new StreamReader(_pipeServer);
                var message = await reader.ReadLineAsync(cancellationToken);

                if (message == "SHOW_WINDOW")
                {
                    Logger.Info("SingleInstanceMessenger", "Received show-window request from another instance");
                    _onShowWindowRequested.Invoke();
                }
                else
                {
                    Logger.Debug("SingleInstanceMessenger", $"Received unknown message: {message}");
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("SingleInstanceMessenger", "Listening cancelled");
                break;
            }
            catch (IOException ex)
            {
                Logger.Warning("SingleInstanceMessenger", $"Pipe communication error: {ex.Message}");
                await Task.Delay(100, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Error("SingleInstanceMessenger", "Error in pipe server", ex);
                await Task.Delay(500, cancellationToken);
            }
            finally
            {
                _pipeServer?.Dispose();
                _pipeServer = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _pipeServer?.Dispose();
            Logger.Debug("SingleInstanceMessenger", "Disposed");
        }
        catch (Exception ex)
        {
            Logger.Debug("SingleInstanceMessenger", $"Dispose error: {ex.Message}");
        }
    }
}
