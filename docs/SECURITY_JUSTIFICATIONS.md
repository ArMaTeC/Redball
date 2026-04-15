# Security Justifications for CodeQL Detections

This document provides justifications for security detections that are intentional design choices or false positives in the Redball codebase.

## Purpose

Redball is a system-level Windows utility that requires:

- Input injection (keep-awake functionality)
- Native Windows API access (P/Invoke)
- Process execution (session helper)

These capabilities trigger CodeQL security alerts, but are required for core functionality.

---

## Intentional Security Features

### 1. Input Injection (Keep-Alive)

**Detection**: `cs/input-injection`
**Files**: `NativeMethods.cs` (SendInput P/Invoke)

**Justification**:
Input injection is the **core functionality** of Redball. The application prevents Windows from sleeping by sending synthetic F15 key presses using the `SendInput` Win32 API.

**Security Controls Implemented**:

- Input is sent only to the current foreground window
- Key presses use scan codes for reliable injection
- Rate limiting prevents excessive input

**CodeQL Suppression Format**:

```csharp
// codeql[cs/input-injection] Required for keep-awake functionality
[DllImport("user32.dll")]
public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
```

---

### 2. Native API Access (P/Invoke)

**Detection**: `cs/dll-import-of-unmanaged-code`
**Files**: `NativeMethods.cs`, `InputInjectionEngine.cs`

**Justification**:
Native API access is required for Windows system integration that is not available via .NET APIs:

1. **Power Management**: `SetThreadExecutionState` (prevent sleep/screen lock)
2. **Input Injection**: `SendInput` (synthetic key presses)
3. **Idle Detection**: `GetLastInputInfo` (detect user activity)
4. **Focus Assist**: `NtQueryWnfStateData` (Windows 10/11 Do Not Disturb detection)
5. **DWM Theming**: `DwmSetWindowAttribute` (Mica/Acrylic backgrounds)
6. **Test Mode Detection**: `NtQuerySystemInformation` (code integrity check)

**Security Controls Implemented**:

- All P/Invoke methods marked with `[SecurityCritical]`
- Buffer size validation before native calls
- Exception handling for restricted environments
- No elevation of privilege through native calls

---

### 3. Process Execution

**Detection**: `cs/command-line-injection`
**File**: `InputInjectionEngine.cs`

**Justification**:
Process execution via `CreateProcessAsUser` is required for RDP session injection. When a user is connected via RDP, the service (running in session 0) must launch a helper process in the target user's session to inject input on their desktop.

**Security Controls Implemented**:

- Helper path validated against whitelist (must be within application directory)
- JSON parameters Base64-encoded to prevent command injection
- Helper executable name strictly validated
- Timeout (5 seconds) to prevent hung processes

---

### 4. Named Pipe IPC

**Detection**: `cs/named-pipe`
**File**: `IpcServer.cs`

**Justification**:
Named pipes provide secure local-only IPC between the WPF UI and Windows Service. This is required because:

1. Services run in session 0, UI runs in interactive session
2. Cross-session communication requires a transport mechanism
3. Named pipes provide built-in Windows authentication

**Security Controls Implemented**:

- Pipe access restricted to Administrators and Interactive Users
- Anonymous access explicitly denied
- Message rate limiting (60 per minute per client)
- Message size limit (1MB max)
- JSON deserialization with strict type constraints
- Client process ID tracking for per-process limits

---

## False Positive Patterns

### SQL Parameterised Queries

**Detection**: Dynamic SQL construction
**File**: `SqliteOutboxStore.cs`

**Analysis**: The dynamic IN clause at line 165 constructs parameter names (`@id0`, `@id1`), **not** values. All values are passed via `AddWithValue()` which properly escapes them. This is secure parameterised query usage, not SQL injection.

### Regex Timeout

**Detection**: Potential ReDoS
**Files**: `ClipboardSanitiser.cs`, `SecurityCIGatesService.cs`

**Analysis**: All regex operations now include a timeout (100-500ms) to prevent ReDoS attacks. Patterns are pre-compiled with `RegexOptions.Compiled` and tested against known-bad inputs.

---

## Approved Suppression Format

Use this format for CodeQL suppression comments:

```csharp
// codeql[cs/detection-id] Brief justification
[SecurityCritical]
[DllImport("library.dll")]
public static extern ReturnType FunctionName(Parameters);
```

Example:

```csharp
// codeql[cs/dll-import-of-unmanaged-code] Required for Windows power management
[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
public static extern uint SetThreadExecutionState(uint esFlags);
```

---

## Validation

To verify these justifications are current:

1. Run CodeQL locally: `codeql database create --language=csharp`
2. Review detections in GitHub Security tab
3. Update this document when adding new P/Invoke or native calls
4. Security controls should be verified in code review

---

## References

- [CodeQL C# Query Help](https://codeql.github.com/codeql-query-help/csharp/)
- [Windows SendInput API](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [CreateProcessAsUser Security](https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw)
- [Named Pipe Security](https://docs.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)

---
