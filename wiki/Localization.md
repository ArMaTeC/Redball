# Localization

Redball supports multiple languages through a built-in internationalization (i18n) system.

## Supported Languages

| Code | Language | Status |
| ---- | -------- | ------ |
| `en` | English | Built-in (default) |
| `es` | Spanish | Built-in |
| `fr` | French | Built-in |
| `de` | German | Built-in |
| `bl` | Bad Language (Cyberpunk) | Built-in |

## How It Works

1. **Embedded locales** are hardcoded in `$script:embeddedLocales` as a JSON string inside `Redball.ps1`
2. **External overrides** can be placed in `locales.json` alongside the script
3. On startup, `Import-RedballLocales` loads embedded locales first, then merges external overrides (external takes precedence)
4. The active locale is determined by:
   - The `Locale` setting in `Redball.json` (if set)
   - The system culture (`(Get-Culture).TwoLetterISOLanguageName`) as auto-detection
   - Falls back to `en` if the detected locale isn't available

## Locale Keys

Each locale provides translations for these keys:

| Key | English Value |
| --- | ------------- |
| `AppName` | Redball |
| `TrayTooltipActive` | Redball (Active) |
| `TrayTooltipPaused` | Redball (Paused) |
| `MenuPause` | Pause Keep-Awake |
| `MenuResume` | Resume Keep-Awake |
| `MenuPreventDisplaySleep` | Prevent Display Sleep |
| `MenuUseF15Heartbeat` | Use F15 Heartbeat |
| `MenuStayAwakeFor` | Stay Awake For |
| `MenuStayAwakeIndefinitely` | Stay Awake Until Paused |
| `MenuExit` | Exit |
| `StatusActive` | Active |
| `StatusPaused` | Paused |
| `StatusDisplayOn` | Display On |
| `StatusDisplayNormal` | Display Normal |
| `StatusF15On` | F15 On |
| `StatusF15Off` | F15 Off |
| `StatusMinLeft` | min left |
| `StatusUnavailable` | Status unavailable |
| `BalloonStarted` | Redball started - keeping system awake |
| `BalloonPaused` | Redball paused |
| `BalloonResumed` | Redball resumed |
| `LogStarted` | Redball started |
| `LogPaused` | Redball paused |
| `LogResumed` | Redball resumed |
| `LogExited` | Redball exited |
| `ErrorIconCreate` | Failed to create icon |
| `ErrorUIUpdate` | UI update failed |
| `ErrorSetActiveState` | Failed to set active state |
| `ErrorSwitchState` | Failed to switch state |
| `ErrorTimedAwake` | Failed to start timed awake |

## Using `Get-LocalizedString`

```powershell
# Get a localized string in the current locale
$text = Get-LocalizedString -Key 'MenuPause'

# Get a string in a specific locale
$text = Get-LocalizedString -Key 'MenuPause' -Locale 'es'
```

Fallback chain: requested locale → English → return key name as-is.

## Adding a New Language

### Method 1: External `locales.json`

Create or edit `locales.json` in the Redball directory:

```json
{
    "pt": {
        "AppName": "Redball",
        "TrayTooltipActive": "Redball (Ativo)",
        "TrayTooltipPaused": "Redball (Pausado)",
        "MenuPause": "Pausar",
        "MenuResume": "Retomar",
        "MenuExit": "Sair"
    }
}
```

Partial translations are supported — any missing keys fall back to English.

### Method 2: Embedded in Script

Add a new locale block to the `$script:embeddedLocales` JSON string in `Redball.ps1` (around line 442).

## Changing Language

### Via Settings GUI

1. Open **Settings...** from the tray menu
2. Go to the **General** tab
3. Change the **Language** dropdown
4. Click **OK**

### Via Configuration File

Edit `Redball.json`:

```json
{
    "Locale": "es"
}
```

### Via Auto-Detection

If `Locale` is not set in the config, Redball auto-detects from `(Get-Culture).TwoLetterISOLanguageName`.

## Implementation Details

- Locales are stored as nested hashtables (converted from `PSCustomObject` via `ConvertTo-Hashtable` for PS 5.1 compatibility)
- The `ConvertTo-Hashtable` helper recursively converts `PSCustomObject` → `Hashtable` and handles arrays
- External locale files are merged key-by-key, so you can override just a few strings
