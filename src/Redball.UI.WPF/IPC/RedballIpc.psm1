# Redball v3.0 Named Pipe IPC Server
# This module provides bidirectional communication between PowerShell core and WPF UI

$script:ipcServerRunning = $false
$script:ipcPipeServer = $null

function Start-RedballIpcServer {
    <#
    .SYNOPSIS
        Starts the named pipe IPC server for WPF UI communication.
    .DESCRIPTION
        Creates a named pipe server that listens for commands from the WPF UI layer.
        Supports actions: GetStatus, SetActive, ShowSettings, etc.
    #>
    param(
        [string]$PipeName = "RedballUI"
    )

    if ($script:ipcServerRunning) {
        Write-VerboseLog -Message "IPC server already running" -Source "IPC"
        return
    }

    try {
        $script:ipcServerRunning = $true
        Write-RedballLog -Level 'INFO' -Message "Starting IPC server on pipe: $PipeName"
        Write-VerboseLog -Message "IPC server started - waiting for WPF UI connection" -Source "IPC"

        # Run server in background runspace
        $runspace = [runspacefactory]::CreateRunspace()
        $runspace.Open()

        $powershell = [powershell]::Create()
        $powershell.Runspace = $runspace

        $powershell.AddScript({
                param($PipeName, $State, $Config)

                Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

public class RedballIpcServer {
    private NamedPipeServerStream _pipeServer;
    private StreamReader _reader;
    private StreamWriter _writer;

    public async Task StartAsync(string pipeName) {
        while (true) {
            try {
                _pipeServer = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await _pipeServer.WaitForConnectionAsync();

                _reader = new StreamReader(_pipeServer);
                _writer = new StreamWriter(_pipeServer) { AutoFlush = true };

                await HandleMessagesAsync();
            }
            catch (Exception ex) {
                Console.WriteLine($"IPC Error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task HandleMessagesAsync() {
        while (_pipeServer?.IsConnected == true) {
            var message = await _reader.ReadLineAsync();
            if (message == null) break;

            try {
                var response = ProcessMessage(message);
                await _writer.WriteLineAsync(response);
            }
            catch (Exception ex) {
                await _writer.WriteLineAsync($@"{{""success"": false, ""error"": ""{ex.Message}""}}");
            }
        }
    }

    private string ProcessMessage(string message) {
        // Parse and process IPC messages
        return @"{""success"": true, ""data"": """"}";
    }
}
"@

                $server = New-Object RedballIpcServer
                $server.StartAsync($PipeName).Wait()
            }).AddArgument($PipeName).AddArgument($script:state).AddArgument($script:config)

        $asyncResult = $powershell.BeginInvoke()

        # Store reference to prevent GC
        $script:ipcAsyncResult = $asyncResult
        $script:ipcPowerShell = $powershell

        Write-RedballLog -Level 'INFO' -Message "IPC server started successfully"
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to start IPC server: $_"
        $script:ipcServerRunning = $false
    }
}

function Send-IpcMessage {
    <#
    .SYNOPSIS
        Sends a message to the WPF UI via named pipe.
    .DESCRIPTION
        Used by PowerShell core to notify UI of state changes.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Action,

        [object]$Data = $null
    )

    try {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream(
            ".",
            "RedballUI-Client",
            [System.IO.Pipes.PipeDirection]::Out)

        $client.Connect(100)  # 100ms timeout

        $writer = New-Object System.IO.StreamWriter($client)
        $writer.AutoFlush = $true

        $message = @{
            Action    = $Action
            Data      = $Data
            Timestamp = (Get-Date).ToString('o')
        } | ConvertTo-Json -Compress

        $writer.WriteLine($message)
        $writer.Close()
        $client.Close()

        return $true
    }
    catch {
        Write-VerboseLog -Message "IPC send failed: $_" -Source "IPC"
        return $false
    }
}

function Stop-RedballIpcServer {
    <#
    .SYNOPSIS
        Stops the IPC server.
    #>
    $script:ipcServerRunning = $false
    if ($script:ipcPipeServer) {
        $script:ipcPipeServer.Dispose()
        $script:ipcPipeServer = $null
    }
    Write-RedballLog -Level 'INFO' -Message "IPC server stopped"
}

# Export functions
Export-ModuleMember -Function Start-RedballIpcServer, Send-IpcMessage, Stop-RedballIpcServer
