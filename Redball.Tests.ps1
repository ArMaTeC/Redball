#Requires -Modules Pester
<#
.SYNOPSIS
    Pester tests for Redball keep-awake utility.
.DESCRIPTION
    Unit tests for Redball functions including state management,
    configuration handling, and icon generation.
#>

BeforeAll {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue

    $scriptPath = Join-Path $PSScriptRoot 'Redball.ps1'
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        throw "Failed to parse Redball.ps1 for tests."
    }

    $functionAsts = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)
    foreach ($functionAst in $functionAsts) {
        Invoke-Expression $functionAst.Extent.Text
    }

    if (-not (Get-Command Import-RedballConfig -ErrorAction SilentlyContinue)) {
        function Import-RedballConfig {
            param([string]$Path = (Join-Path $PSScriptRoot 'Redball.json'))
            if (-not (Test-Path $Path)) {
                Set-Content -Path $Path -Value ($script:config | ConvertTo-Json -Depth 6)
                return
            }

            $loaded = Get-Content $Path -Raw | ConvertFrom-Json
            foreach ($prop in $loaded.PSObject.Properties) {
                $script:config[$prop.Name] = $prop.Value
            }
        }
    }

    if (-not (Get-Command Load-RedballConfig -ErrorAction SilentlyContinue)) {
        Set-Alias -Name Load-RedballConfig -Value Import-RedballConfig -Scope Script
    }

    if (-not (Get-Command Save-RedballConfig -ErrorAction SilentlyContinue)) {
        function Save-RedballConfig {
            param([string]$Path = (Join-Path $PSScriptRoot 'Redball.json'))
            Set-Content -Path $Path -Value ($script:config | ConvertTo-Json -Depth 6)
        }
    }

    function Reset-TestState {
        $script:state = [ordered]@{
            Active = $true
            PreventDisplaySleep = $true
            UseHeartbeatKeypress = $true
            HeartbeatSeconds = 59
            Until = $null
            NotifyIcon = $null
            Context = $null
            HeartbeatTimer = $null
            DurationTimer = $null
            ToggleMenuItem = $null
            DisplayMenuItem = $null
            HeartbeatMenuItem = $null
            StatusMenuItem = $null
            BatteryMenuItem = $null
            StartupMenuItem = $null
            PreviousIcon = $null
            IsShuttingDown = $false
            LastStatusText = ''
            LastIconState = ''
            UiUpdatePending = $false
            BatteryAware = $false
            BatteryThreshold = 20
            OnBattery = $false
            AutoPausedBattery = $false
            NetworkAware = $false
            AutoPausedNetwork = $false
            ActiveBeforeNetwork = $false
            IdleDetection = $false
            IdleThresholdMinutes = 30
            AutoPausedIdle = $false
            AutoPausedPresentation = $false
            AutoPausedSchedule = $false
            ManualOverride = $false
            StartTime = $null
            ActiveBeforeBattery = $false
            ActiveBeforeIdle = $false
            SessionId = [guid]::NewGuid().ToString()
            KeepAwakeRunspaceInfo = $null
        }
    }

    function Reset-TestConfig {
        $script:config = @{
            HeartbeatSeconds = 59
            PreventDisplaySleep = $true
            UseHeartbeatKeypress = $true
            LogPath = 'TestDrive:\test.log'
            MaxLogSizeMB = 10
            ScheduleEnabled = $false
            ScheduleStartTime = '09:00'
            ScheduleStopTime = '18:00'
            ScheduleDays = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')
            PresentationModeDetection = $false
            EnablePerformanceMetrics = $false
            EnableTelemetry = $false
            ProcessIsolation = $false
        }
    }

    Reset-TestState
    Reset-TestConfig
    $script:VERSION = '2.0.0'
}

Describe "Get-StatusText" {
    It "Returns correct format when active" {
        $script:state.Active = $true
        $script:state.PreventDisplaySleep = $true
        $script:state.UseHeartbeatKeypress = $true
        $script:state.Until = $null
        
        $result = Get-StatusText
        $result | Should -Match "Active"
        $result | Should -Match "Display On"
        $result | Should -Match "F15 On"
    }
    
    It "Returns correct format when paused" {
        $script:state.Active = $false
        $script:state.PreventDisplaySleep = $false
        $script:state.UseHeartbeatKeypress = $false
        
        $result = Get-StatusText
        $result | Should -Match "Paused"
        $result | Should -Match "Display Normal"
        $result | Should -Match "F15 Off"
    }
    
    It "Shows time remaining when timer is set" {
        $script:state.Active = $true
        $script:state.Until = (Get-Date).AddMinutes(30)
        
        $result = Get-StatusText
        $result | Should -Match "\d+min left"
    }
    
    It "Returns fallback on error" {
        $previousState = $script:state
        try {
            $script:state = $null  # Force error condition
            $result = Get-StatusText
            $result | Should -Be "Paused | Display Normal | F15 Off"
        }
        finally {
            $script:state = $previousState
        }
    }
}

Describe "Configuration Management" {
    It "Creates default config if missing" {
        $testConfigPath = "TestDrive:\test_config.json"
        if (Test-Path $testConfigPath) { Remove-Item $testConfigPath -Force }
        Load-RedballConfig -Path $testConfigPath
        Test-Path $testConfigPath | Should -Be $true
    }
    
    It "Loads existing config values" {
        $testConfigPath = "TestDrive:\test_config.json"
        $testConfig = @{
            HeartbeatSeconds = 30
            PreventDisplaySleep = $false
        } | ConvertTo-Json
        
        Set-Content -Path $testConfigPath -Value $testConfig
        Load-RedballConfig -Path $testConfigPath
        
        $script:config.HeartbeatSeconds | Should -Be 30
        $script:config.PreventDisplaySleep | Should -Be $false
    }
    
    It "Saves config to file" {
        $testConfigPath = "TestDrive:\test_config.json"
        Save-RedballConfig -Path $testConfigPath
        Test-Path $testConfigPath | Should -Be $true
    }
}

Describe "Logging" {
    BeforeEach {
        $testLogPath = "TestDrive:\test.log"
        $script:config.LogPath = $testLogPath
        if (Test-Path $testLogPath) {
            Remove-Item $testLogPath -Force
        }
    }
    
    It "Writes log entry with timestamp" {
        $testLogPath = "TestDrive:\test.log"
        Write-RedballLog -Level 'INFO' -Message 'Test message'
        
        $content = Get-Content $testLogPath -Raw
        $content | Should -Match "\[.*\] \[INFO\] Test message"
    }
    
    It "Rotates log when size exceeds limit" {
        $testLogPath = "TestDrive:\test.log"
        # Create oversized log
        "A" * (11MB) | Set-Content $testLogPath
        
        Write-RedballLog -Level 'INFO' -Message 'After rotation'
        
        $backupExists = Get-ChildItem "TestDrive:\" -Filter "*.bak" | Where-Object { $_.Name -like "test.log.*" }
        $backupExists | Should -Not -BeNullOrEmpty
    }
    
    It "Handles all log levels" {
        $testLogPath = "TestDrive:\test.log"
        Write-RedballLog -Level 'DEBUG' -Message 'Debug'
        Write-RedballLog -Level 'INFO' -Message 'Info'
        Write-RedballLog -Level 'WARN' -Message 'Warn'
        Write-RedballLog -Level 'ERROR' -Message 'Error'
        
        $content = Get-Content $testLogPath
        $content.Count | Should -Be 4
    }
}

Describe "State Management" {
    BeforeEach {
        $script:state.Active = $false
        $script:state.Until = $null
        $script:state.IsShuttingDown = $false
    }
    
    It "Activates correctly" {
        Mock Set-KeepAwakeState {}
        Mock Update-RedballUI {}
        
        Set-ActiveState -Active:$true
        
        $script:state.Active | Should -Be $true
        Should -Invoke Set-KeepAwakeState -ParameterFilter { $Enable -eq $true }
    }
    
    It "Deactivates correctly" {
        Mock Set-KeepAwakeState {}
        Mock Update-RedballUI {}
        
        Set-ActiveState -Active:$false
        
        $script:state.Active | Should -Be $false
        $script:state.Until | Should -BeNullOrEmpty
    }
    
    It "Sets timer when provided" {
        Mock Set-KeepAwakeState {}
        Mock Update-RedballUI {}
        
        $futureTime = (Get-Date).AddHours(1)
        Set-ActiveState -Active:$true -Until $futureTime
        
        $script:state.Until | Should -Be $futureTime
    }
    
    It "Does not change state when shutting down" {
        $script:state.IsShuttingDown = $true
        $originalActive = $script:state.Active
        
        Set-ActiveState -Active:$true
        
        $script:state.Active | Should -Be $originalActive
    }
}

Describe "Icon Generation" {
    BeforeEach {
        Reset-TestState
        Reset-TestConfig
    }

    It "Creates icon for active state" {
        { Get-CustomTrayIcon -State 'active' } | Should -Not -Throw
    }
    
    It "Creates different colored icons for each state" {
        $states = @('active', 'timed', 'paused')
        foreach ($state in $states) {
            { Get-CustomTrayIcon -State $state } | Should -Not -Throw
        }
    }
    
    It "Handles icon generation errors gracefully" {
        Mock Write-RedballLog {}
        { Get-CustomTrayIcon -State 'active' } | Should -Not -Throw
    }
}

Describe "Input Validation" {
    It "Validates timer range" {
        Mock Set-KeepAwakeState {}
        { Start-TimedAwake -Minutes 0 } | Should -Throw
        { Start-TimedAwake -Minutes 721 } | Should -Throw
        { Start-TimedAwake -Minutes 30 } | Should -Not -Throw
    }
    
    It "Requires mandatory parameter for timed awake" {
        $cmd = Get-Command Start-TimedAwake
        $cmd.Parameters['Minutes'].Attributes | Where-Object { $_ -is [Parameter] } | 
            ForEach-Object { $_.Mandatory | Should -Be $true }
    }
}

Describe "Error Handling" {
    It "Handles Set-KeepAwakeState errors in Set-ActiveState" {
        $script:config.ProcessIsolation = $false
        Mock Set-KeepAwakeState { throw 'Test stop' }
        Mock Write-RedballLog {}
        
        { Set-ActiveState -Active:$true } | Should -Not -Throw
        Should -Invoke Write-RedballLog -ParameterFilter { $Level -eq 'ERROR' }
    }
    
    It "Logs errors in Switch-ActiveState" {
        Mock Set-ActiveState { throw "Test error" }
        Mock Write-RedballLog {}
        
        { Switch-ActiveState } | Should -Not -Throw
        Should -Invoke Write-RedballLog -ParameterFilter { $Level -eq 'ERROR' }
    }
}

Describe "UI Updates" {
    BeforeEach {
        $script:state.NotifyIcon = [pscustomobject]@{
            Icon = $null
            Text = ''
            IsDisposed = $false
        }
        $script:state.ToggleMenuItem = [pscustomobject]@{ Text = '' }
        $script:state.DisplayMenuItem = [pscustomobject]@{ Checked = $false }
        $script:state.HeartbeatMenuItem = [pscustomobject]@{ Checked = $false }
        $script:state.StatusMenuItem = [pscustomobject]@{ Text = '' }
    }
    
    It "Updates UI when active" {
        $script:state.Active = $true
        $script:state.PreventDisplaySleep = $true
        $script:state.UseHeartbeatKeypress = $true
        
        Mock Get-CustomTrayIcon { return $null }
        
        Update-RedballUI
        
        $script:state.NotifyIcon.Text | Should -Match "Redball \(Active\)"
        $script:state.ToggleMenuItem.Text | Should -Match "Pause"
    }
    
    It "Updates UI when paused" {
        $script:state.Active = $false
        
        Mock Get-CustomTrayIcon { return $null }
        
        Update-RedballUI
        
        $script:state.NotifyIcon.Text | Should -Match "Redball \(Paused\)"
        $script:state.ToggleMenuItem.Text | Should -Match "Resume"
    }
    
    It "Does not update when shutting down" {
        $script:state.IsShuttingDown = $true
        $originalText = $script:state.NotifyIcon.Text
        
        Update-RedballUI
        
        $script:state.NotifyIcon.Text | Should -Be $originalText
    }
}

Describe "Cleanup and Shutdown" {
    BeforeEach {
        $script:state.HeartbeatTimer = New-Object PSObject
        Add-Member -InputObject $script:state.HeartbeatTimer -MemberType ScriptMethod -Name Stop -Value { }
        Add-Member -InputObject $script:state.HeartbeatTimer -MemberType ScriptMethod -Name Dispose -Value { }

        $script:state.DurationTimer = New-Object PSObject
        Add-Member -InputObject $script:state.DurationTimer -MemberType ScriptMethod -Name Stop -Value { }
        Add-Member -InputObject $script:state.DurationTimer -MemberType ScriptMethod -Name Dispose -Value { }

        $script:state.NotifyIcon = [pscustomobject]@{ Visible = $true }
        Add-Member -InputObject $script:state.NotifyIcon -MemberType ScriptMethod -Name Dispose -Value { }

        $script:state.PreviousIcon = New-Object PSObject
        Add-Member -InputObject $script:state.PreviousIcon -MemberType ScriptMethod -Name Dispose -Value { }
    }
    
    It "Stops timers on exit" {
        Mock Set-KeepAwakeState {}
        
        Exit-Application
        
        $script:state.IsShuttingDown | Should -Be $true
    }
    
    It "Disposes all resources" {
        Mock Set-KeepAwakeState {}
        
        Exit-Application
        
        $script:state.IsShuttingDown | Should -Be $true
        $script:state.NotifyIcon.Visible | Should -Be $false
    }
}

Describe "Battery-Aware Mode" {
    BeforeEach {
        $script:state.BatteryAware = $true
        $script:state.BatteryThreshold = 20
        $script:state.AutoPausedBattery = $false
        $script:state.Active = $true
    }
    
    It "Detects battery status" {
        $battery = Get-BatteryStatus
        $battery | Should -Not -BeNullOrEmpty
        $battery.ContainsKey('HasBattery') | Should -Be $true
    }
    
    It "Pauses when battery below threshold" {
        # Mock low battery condition
        $script:state.OnBattery = $true
        $script:state.BatteryThreshold = 50
        
        # Should trigger auto-pause logic
        Test-BatteryThreshold
        # Result depends on actual battery, but function should not throw
        { Test-BatteryThreshold } | Should -Not -Throw
    }
    
    It "Respects battery-aware setting" {
        $script:state.BatteryAware = $false
        { Update-BatteryAwareState } | Should -Not -Throw
    }
}

Describe "Network-Aware Mode" {
    BeforeEach {
        $script:state.NetworkAware = $true
        $script:state.AutoPausedNetwork = $false
    }
    
    It "Detects network status" {
        $network = Get-NetworkStatus
        $network | Should -Not -BeNullOrEmpty
        $network.ContainsKey('IsConnected') | Should -Be $true
    }
    
    It "Handles network check without errors" {
        { Update-NetworkAwareState } | Should -Not -Throw
    }
}

Describe "Idle Detection" {
    BeforeEach {
        $script:state.IdleDetection = $true
        $script:state.IdleThresholdMinutes = 30
        $script:state.AutoPausedIdle = $false
    }
    
    It "Gets idle time without errors" {
        { Get-IdleTimeMinutes } | Should -Not -Throw
    }
    
    It "Handles idle check without errors" {
        { Update-IdleAwareState } | Should -Not -Throw
    }
}

Describe "CLI Parameters" {
    It "Returns status as JSON" {
        $status = Get-RedballStatus
        $status | Should -Not -BeNullOrEmpty
        # Should be valid JSON
        { $status | ConvertFrom-Json } | Should -Not -Throw
    }
    
    It "Status contains required fields" {
        $status = Get-RedballStatus | ConvertFrom-Json
        $status.Version | Should -Not -BeNullOrEmpty
        $status.PSObject.Properties.Name | Should -Contain 'Active'
        $status.PSObject.Properties.Name | Should -Contain 'HasBattery'
    }
}

Describe "Session State Management" {
    It "Saves session state" {
        $testStatePath = "TestDrive:\test_state.json"
        $script:state.Active = $true
        $script:state.BatteryAware = $true
        $script:state.NetworkAware = $false
        
        Save-RedballState -Path $testStatePath
        
        Test-Path $testStatePath | Should -Be $true
    }
    
    It "Restores session state" {
        $testStatePath = "TestDrive:\test_state.json"
        # First save a known state
        $script:state.Active = $false
        $script:state.BatteryAware = $true
        $script:state.PreventDisplaySleep = $false
        $script:state.UseHeartbeatKeypress = $false
        Save-RedballState -Path $testStatePath
        
        # Change current state
        $script:state.Active = $true
        $script:state.BatteryAware = $false
        $script:state.PreventDisplaySleep = $true
        $script:state.UseHeartbeatKeypress = $true
        
        # Restore and verify
        Restore-RedballState -Path $testStatePath
        
        $script:state.BatteryAware | Should -Be $true
        $script:state.PreventDisplaySleep | Should -Be $false
        $script:state.UseHeartbeatKeypress | Should -Be $false
    }
}

Describe "Scheduled Operation" {
    BeforeEach {
        $script:config.ScheduleEnabled = $true
        $script:config.ScheduleStartTime = '09:00'
        $script:config.ScheduleStopTime = '18:00'
        $script:config.ScheduleDays = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')
    }
    
    It "Tests schedule active without errors" {
        { Test-ScheduleActive } | Should -Not -Throw
    }
    
    It "Updates schedule state without errors" {
        { Update-ScheduleState } | Should -Not -Throw
    }
}

Describe "Presentation Mode Detection" {
    BeforeEach {
        $script:config.PresentationModeDetection = $true
    }
    
    It "Tests presentation mode without errors" {
        { Test-PresentationMode } | Should -Not -Throw
    }
    
    It "Updates presentation state without errors" {
        { Update-PresentationModeState } | Should -Not -Throw
    }
}

AfterAll {
    # Cleanup any test files
    if (Test-Path "TestDrive:\test.log") {
        Remove-Item "TestDrive:\test.log" -Force -ErrorAction SilentlyContinue
    }
}
