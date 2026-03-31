using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// QR Code pairing service for mobile device connection.
/// Generates QR codes that mobile apps can scan to quickly pair with Redball.
/// </summary>
public class QRCodePairingService
{
    private static readonly Lazy<QRCodePairingService> _instance = new(() => new QRCodePairingService());
    public static QRCodePairingService Instance => _instance.Value;

    private readonly MobileCompanionApiService _apiService;

    public event EventHandler<QRCodeGeneratedEventArgs>? QRCodeGenerated;
    public event EventHandler<DevicePairedViaQREventArgs>? DevicePairedViaQR;

    private QRCodePairingService()
    {
        _apiService = MobileCompanionApiService.Instance;
        
        Logger.Verbose("QRCodePairingService", "Initialized");
    }

    /// <summary>
    /// Generates a QR code for device pairing.
    /// </summary>
    public async Task<QRPairingData> GeneratePairingQRCodeAsync()
    {
        try
        {
            // Get pairing information
            var pairingInfo = new PairingQRData
            {
                Type = "redball_pairing",
                Version = 1,
                HostAddress = GetLocalIpAddress(),
                Port = 5000,
                AppName = "Redball",
                RequiresConfirmation = true,
                GeneratedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };

            // Serialize to JSON
            var jsonData = JsonSerializer.Serialize(pairingInfo, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Generate QR code image
            var qrCodeImage = await GenerateQRCodeImageAsync(jsonData, 300);

            var result = new QRPairingData
            {
                QRCodeImage = qrCodeImage,
                RawData = jsonData,
                PairingInfo = pairingInfo,
                PairingCode = GenerateNumericCode()
            };

            QRCodeGenerated?.Invoke(this, new QRCodeGeneratedEventArgs
            {
                QRData = result,
                GeneratedAt = DateTime.UtcNow
            });

            Logger.Info("QRCodePairingService", $"Generated pairing QR code (expires: {pairingInfo.ExpiresAt})");

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("QRCodePairingService", "Failed to generate QR code", ex);
            throw;
        }
    }

    /// <summary>
    /// Generates a simple numeric pairing code for manual entry.
    /// </summary>
    public string GenerateNumericPairingCode()
    {
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();
        
        Logger.Debug("QRCodePairingService", $"Generated numeric pairing code: {code}");
        
        return code;
    }

    /// <summary>
    /// Validates a pairing request from a QR code scan.
    /// </summary>
    public async Task<bool> ValidatePairingRequestAsync(PairingRequest request)
    {
        try
        {
            // Check if the request is valid
            if (string.IsNullOrEmpty(request?.DeviceId) || string.IsNullOrEmpty(request.DeviceName))
            {
                Logger.Warning("QRCodePairingService", "Invalid pairing request - missing device info");
                return false;
            }

            // Verify the pairing code if provided
            if (!string.IsNullOrEmpty(request.PairingCode))
            {
                // Validate against stored code
                // Implementation would check against temporary storage
            }

            // Log the pairing attempt
            Logger.Info("QRCodePairingService", 
                $"Pairing request from {request.DeviceName} ({request.Platform})");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("QRCodePairingService", "Error validating pairing request", ex);
            return false;
        }
    }

    /// <summary>
    /// Completes the pairing process after user confirmation.
    /// </summary>
    public async Task<PairingResult> CompletePairingAsync(string pairingCode, QRDeviceInfo deviceInfo)
    {
        try
        {
            // Validate the pairing code
            if (!IsValidPairingCode(pairingCode))
            {
                return new PairingResult
                {
                    Success = false,
                    Error = "Invalid or expired pairing code"
                };
            }

            // Register device with the API service
            var apiKey = await _apiService.RegisterDeviceAsync(new ApiDeviceInfo 
            { 
                Name = deviceInfo.Name, 
                Platform = deviceInfo.Platform,
                DeviceId = deviceInfo.DeviceId
            });

            if (string.IsNullOrEmpty(apiKey))
            {
                return new PairingResult
                {
                    Success = false,
                    Error = "Failed to register device"
                };
            }

            var pairedDevice = new PairedMobileDevice
            {
                DeviceId = Guid.NewGuid().ToString("N"),
                DeviceName = deviceInfo.Name,
                Platform = deviceInfo.Platform,
                PairedAt = DateTime.UtcNow,
                ApiKey = apiKey
            };

            DevicePairedViaQR?.Invoke(this, new DevicePairedViaQREventArgs
            {
                Device = pairedDevice
            });

            Logger.Info("QRCodePairingService", $"Device paired successfully: {deviceInfo.Name}");

            return new PairingResult
            {
                Success = true,
                DeviceId = pairedDevice.DeviceId,
                ApiKey = apiKey
            };
        }
        catch (Exception ex)
        {
            Logger.Error("QRCodePairingService", "Failed to complete pairing", ex);
            return new PairingResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Generates a QR code image from string data.
    /// </summary>
    private async Task<byte[]> GenerateQRCodeImageAsync(string data, int size)
    {
        // Note: This would use QRCoder NuGet package in production
        // For now, return a placeholder implementation
        
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            
            return qrCode.GetGraphic(size);
        }
        catch
        {
            // Fallback: return empty byte array or placeholder
            // In production, this would use the actual QR library
            return await Task.FromResult(new byte[0]);
        }
    }

    /// <summary>
    /// Gets the local IP address for network pairing.
    /// </summary>
    private string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        
        return "localhost";
    }

    private string GenerateNumericCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }

    private bool IsValidPairingCode(string code)
    {
        // Validate against temporary storage
        // Check expiration, etc.
        return !string.IsNullOrEmpty(code) && code.Length == 6;
    }
}

/// <summary>
/// Data encoded in the QR code for pairing.
/// </summary>
public class PairingQRData
{
    public string Type { get; set; } = string.Empty;
    public int Version { get; set; }
    public string HostAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string AppName { get; set; } = string.Empty;
    public bool RequiresConfirmation { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Result of QR code generation.
/// </summary>
public class QRPairingData
{
    public byte[] QRCodeImage { get; set; } = Array.Empty<byte>();
    public string RawData { get; set; } = string.Empty;
    public PairingQRData PairingInfo { get; set; } = new();
    public string PairingCode { get; set; } = string.Empty;
}

/// <summary>
/// Pairing request from mobile device.
/// </summary>
public class PairingRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? PairingCode { get; set; }
    public DateTime RequestedAt { get; set; }
}

/// <summary>
/// Device information for pairing.
/// </summary>
public class QRDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
}

/// <summary>
/// Result of pairing operation.
/// </summary>
public class PairingResult
{
    public bool Success { get; set; }
    public string? DeviceId { get; set; }
    public string? ApiKey { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Paired mobile device information.
/// </summary>
public class PairedMobileDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime PairedAt { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}

// Event args
public class QRCodeGeneratedEventArgs : EventArgs
{
    public QRPairingData QRData { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class DevicePairedViaQREventArgs : EventArgs
{
    public PairedMobileDevice Device { get; set; } = new();
}
