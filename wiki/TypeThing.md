# TypeThing — Clipboard Typer

TypeThing is a built-in feature of Redball that simulates human-like typing of clipboard contents. It reads text from the clipboard and types it character-by-character using the Windows `SendInput` API with `KEYEVENTF_UNICODE`, making it compatible with virtually any application and character set.

## Overview

- **Purpose:** Paste text into applications that block Ctrl+V by typing it character-by-character
- **Activation:** Global hotkey (default: Ctrl+Shift+V) or tray menu
- **Emergency Stop:** Global hotkey (default: Ctrl+Shift+X) or tray menu
- **Realism:** Configurable random delays and pauses simulate natural typing

## How It Works

1. Press **Ctrl+Shift+V** (or your configured start hotkey)
2. Redball reads the clipboard text
3. If clipboard is empty, a warning notification is shown
4. If clipboard has >10,000 characters, a confirmation dialog appears
5. A countdown begins (default: 3 seconds) — switch to your target application during this time
6. Characters are typed one at a time with random delays between keystrokes
7. Typing progress is shown in the tray menu status
8. Press **Ctrl+Shift+X** to emergency-stop at any time

## Character Handling

| Character | Behaviour |
| --------- | --------- |
| Regular characters | Sent via `KEYEVENTF_UNICODE` (full Unicode support) |
| Newline (`\n`) | Sends `VK_RETURN` keypress (if `TypeThingTypeNewlines` is enabled) |
| Carriage return + newline (`\r\n`) | Sends single `VK_RETURN` (skips `\r` to avoid double-Enter) |
| Tab (`\t`) | Sends `VK_TAB` keypress |
| Other control characters | Silently skipped |

## Configuration

TypeThing settings are persisted in the main Redball config (registry `HKCU\Software\Redball\UserData` with local copy `%LocalAppData%\Redball\UserData\Redball.json`):

| Setting | Type | Default | Description |
| ------- | ---- | ------- | ----------- |
| `TypeThingEnabled` | bool | `true` | Master switch for the TypeThing feature |
| `TypeThingMinDelayMs` | int | `30` | Minimum delay between keystrokes (ms) |
| `TypeThingMaxDelayMs` | int | `120` | Maximum delay between keystrokes (ms) |
| `TypeThingStartDelaySec` | int | `3` | Countdown seconds before typing begins |
| `TypeThingStartHotkey` | string | `Ctrl+Shift+V` | Global hotkey to start typing |
| `TypeThingStopHotkey` | string | `Ctrl+Shift+X` | Global hotkey to stop typing |
| `TypeThingTheme` | string | `dark` | Settings dialog theme (`light`, `dark`, `hacker`) |
| `TypeThingAddRandomPauses` | bool | `true` | Add occasional longer pauses for realism |
| `TypeThingRandomPauseChance` | int | `5` | Chance (%) per character of a random pause |
| `TypeThingRandomPauseMaxMs` | int | `500` | Maximum random pause duration (ms) |
| `TypeThingTypeNewlines` | bool | `true` | Press Enter when a newline is encountered |
| `TypeThingNotifications` | bool | `true` | Show tray notifications for typing events |
| `TypeThingInputMode` | string | `SendInput` | Input method (`SendInput` or `Interception`) |
| `TypeThingTtsEnabled` | bool | `false` | Enable text-to-speech while typing |

## Typing Speed

The typing speed is controlled by `TypeThingMinDelayMs` and `TypeThingMaxDelayMs`. Each character's delay is randomly chosen between these two values.

Approximate words per minute (WPM) for common settings:

| Min Delay | Max Delay | Approx WPM |
| --------- | --------- | ----------- |
| 10 ms | 50 ms | ~400 WPM |
| 30 ms | 120 ms | ~160 WPM |
| 50 ms | 200 ms | ~96 WPM |
| 80 ms | 250 ms | ~73 WPM |
| 100 ms | 300 ms | ~60 WPM |

The formula is: `WPM ≈ 60000 / ((minDelay + maxDelay) / 2) / 5`

## Random Pauses

When `TypeThingAddRandomPauses` is enabled, there is a `TypeThingRandomPauseChance`% probability per character of adding an extra pause of up to `TypeThingRandomPauseMaxMs` milliseconds. This simulates the natural pauses that occur during human typing (e.g., thinking, reading ahead).

## Hotkey Format

Hotkeys are specified as `Modifier+Modifier+Key` strings. Supported modifiers:

- `Ctrl` (or `Control`)
- `Alt`
- `Shift`
- `Win`

Supported keys: `A`–`Z`, `0`–`9`, `F1`–`F12`, `Pause`, `Space`, `Escape`, `Enter`, `Tab`, `Insert`, `Delete`, `Home`, `End`, `PageUp`, `PageDown`, arrow keys, and numpad keys.

Examples: `Ctrl+Shift+V`, `Ctrl+Alt+T`, `Win+Shift+F5`

## Settings Dialog

TypeThing has its own dedicated settings dialog, accessible from:

- **Tray Menu → TypeThing → TypeThing Settings...**
- **Tray Menu → Settings... → TypeThing tab**

The dedicated dialog groups settings into four sections:

1. **Typing Speed** — Min/max delay, start delay, live WPM estimate
2. **Behaviour** — Random pauses, newlines, notifications
3. **Hotkeys** — Click a field and press your desired key combination
4. **Appearance** — Theme selector with live preview

### Themes

| Theme | Description |
| ----- | ----------- |
| `light` | Light background, standard controls |
| `dark` | Dark background with blue accents (default) |
| `hacker` | Black background with green text, Consolas font |

The theme is applied to the TypeThing settings dialog controls.

## Tray Menu Integration

When `TypeThingEnabled` is `true`, a **TypeThing** submenu appears in the Redball tray menu:

- **Type Clipboard** (Ctrl+Shift+V) — Start typing
- **Stop Typing** (Ctrl+Shift+X) — Emergency stop (disabled when not typing)
- **Status: Idle** — Shows current typing progress (e.g., "Typing 150/500 chars...")
- **TypeThing Settings...** — Open the settings dialog

## Runtime State

TypeThing tracks its state internally in `MainWindow.TypeThing.cs`:

- **IsTyping** — Whether typing is currently in progress
- **ShouldStop** — Emergency stop has been requested
- **CurrentIndex / TotalChars** — Progress tracking
- **CountdownRemaining** — Seconds remaining before typing starts
- **HotkeysRegistered** — Whether global hotkeys are active

## Security Considerations

- Clipboard text is held in memory only during active typing
- The text is explicitly cleared when typing stops or completes
- TypeThing does not persist clipboard content to disk
- Large clipboard warnings (>10,000 characters) prevent accidental long-running operations

## Interop Details

TypeThing uses native Win32 APIs via P/Invoke in `Interop/NativeMethods.cs`:

- **`SendInput`** (`user32.dll`) — Sends keyboard input with `KEYEVENTF_UNICODE` for full Unicode support
- **`RegisterHotKey` / `UnregisterHotKey`** (`user32.dll`) — Global hotkey registration via `HotkeyService`
- **`INPUT` / `KEYBDINPUT`** — Win32 structures for keyboard input simulation

The `SendInput` approach ensures compatibility with applications that don't accept `WM_CHAR` messages.

## Text-to-Speech

When `TypeThingTtsEnabled` is `true`, typed text is also spoken aloud using the `TextToSpeechService`, which wraps the Windows `System.Speech.Synthesis.SpeechSynthesizer` API.
