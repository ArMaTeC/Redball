using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Lightweight local-only HTTP API for querying Redball status and controlling it remotely.
/// Listens on localhost:48080 by default.
/// Endpoints:
///   GET /status    — returns JSON with current keep-awake state, uptime, config summary
///   POST /activate — activates keep-awake
///   POST /pause    — pauses keep-awake
///   GET /health    — simple health check
/// </summary>
public class WebApiService
{
    private static readonly Lazy<WebApiService> _instance = new(() => new WebApiService());
    public static WebApiService Instance => _instance.Value;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _running;

    public bool IsRunning => _running;
    public int Port { get; private set; } = 48080;

    private WebApiService()
    {
        Logger.Verbose("WebApiService", "Instance created");
    }

    public void Start(int port = 48080)
    {
        if (_running) return;

        Port = port;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _running = true;
            _cts = new CancellationTokenSource();

            Task.Run(() => ListenLoop(_cts.Token));
            Logger.Info("WebApiService", $"Local API started on http://localhost:{port}/");
        }
        catch (Exception ex)
        {
            Logger.Error("WebApiService", $"Failed to start API on port {port}", ex);
            _running = false;
        }
    }

    public void Stop()
    {
        if (!_running) return;

        _running = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        Logger.Info("WebApiService", "Local API stopped");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Logger.Debug("WebApiService", $"Listener error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath?.ToLowerInvariant() ?? "/";
            var method = request.HttpMethod.ToUpperInvariant();

            string json;
            int statusCode = 200;

            switch (path)
            {
                case "/health":
                    json = JsonSerializer.Serialize(new { status = "ok", timestamp = DateTime.UtcNow });
                    break;

                case "/status":
                    var keepAwake = KeepAwakeService.Instance;
                    var config = ConfigService.Instance.Config;
                    json = JsonSerializer.Serialize(new
                    {
                        active = keepAwake.IsActive,
                        uptime = ScheduledRestartService.Instance.Uptime.ToString(),
                        preventDisplaySleep = config.PreventDisplaySleep,
                        heartbeatInput = config.HeartbeatInputMode,
                        batteryAware = config.BatteryAware,
                        networkAware = config.NetworkAware,
                        vpnConnected = new NetworkMonitorService().IsVpnConnected(),
                        cpuTemp = TemperatureMonitorService.Instance.CurrentCpuTemp,
                        sessions = SessionStatsService.Instance.TotalSessions,
                        timestamp = DateTime.UtcNow
                    });
                    break;

                case "/activate":
                    if (method == "POST")
                    {
                        KeepAwakeService.Instance.SetActive(true);
                        json = JsonSerializer.Serialize(new { result = "activated" });
                    }
                    else
                    {
                        statusCode = 405;
                        json = JsonSerializer.Serialize(new { error = "Use POST" });
                    }
                    break;

                case "/pause":
                    if (method == "POST")
                    {
                        KeepAwakeService.Instance.SetActive(false);
                        json = JsonSerializer.Serialize(new { result = "paused" });
                    }
                    else
                    {
                        statusCode = 405;
                        json = JsonSerializer.Serialize(new { error = "Use POST" });
                    }
                    break;

                default:
                    statusCode = 404;
                    json = JsonSerializer.Serialize(new
                    {
                        error = "Not found",
                        endpoints = new[] { "GET /health", "GET /status", "POST /activate", "POST /pause" }
                    });
                    break;
            }

            var buffer = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.Headers.Add("Access-Control-Allow-Origin", "http://localhost");
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Logger.Debug("WebApiService", $"Request handler error: {ex.Message}");
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }
}
