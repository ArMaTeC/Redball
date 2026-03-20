# Localization

Redball supports multiple languages through a built-in internationalization (i18n) system managed by `LocalizationService`.

## Supported Languages

| Code | Language | Status |
| ---- | -------- | ------ |
| `en` | English | Built-in (default) |
| `es` | Spanish | Built-in |
| `fr` | French | Built-in |
| `de` | German | Built-in |
| `bl` | Bad Language (Cyberpunk) | Built-in |

## How It Works

1. **Embedded locales** are built into the `LocalizationService` class
2. **External overrides** can be placed in `locales.json` alongside the application
3. On startup, `LocalizationService` loads embedded locales first, then merges external overrides (external takes precedence)
4. The active locale is determined by:
   - The `Locale` setting in `Redball.json` (if set)
   - Falls back to `en` if the configured locale isn't available

## LocalizationService API

```csharp
// Get a localized string
string text = LocalizationService.Instance.GetString("MenuPause");

// Change locale
LocalizationService.Instance.SetLocale("es");

// List available locales
string[] locales = LocalizationService.Instance.GetAvailableLocales();
```

Fallback chain: requested locale → English → return key name as-is.

## Adding a New Language

### External `locales.json`

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

## Changing Language

### Via Settings GUI

1. Open the main window → **Settings** section
2. Change the **Language** dropdown
3. Click **Apply Settings**

### Via Configuration File

Edit `Redball.json`:

```json
{
    "Locale": "es"
}
```
