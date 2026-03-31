# Browser Extension Icons

This folder contains the extension icons for the Redball browser companion.

## Structure

```
icons/
├── icon16.png       # Main extension icon (16x16)
├── icon32.png       # Main extension icon (32x32)
├── icon48.png       # Main extension icon (48x48)
├── icon128.png      # Main extension icon (128x128)
├── active/          # Icons when keep-awake is active
│   ├── icon16.png
│   ├── icon32.png
│   ├── icon48.png
│   └── icon128.png
└── inactive/        # Icons when keep-awake is inactive
    ├── icon16.png
    ├── icon32.png
    ├── icon48.png
    └── icon128.png
```

## Icon Design

The Redball icon should be a simple, recognizable symbol:
- **Active state**: Green/red ball or glowing indicator
- **Inactive state**: Gray/blue neutral ball

## Generating Icons

You can generate these icons from the main Redball logo using:

```powershell
# PowerShell script to resize icons
$source = "..\..\Assets\redball-logo.png"
$sizes = @(16, 32, 48, 128)

foreach ($size in $sizes) {
    # Requires ImageMagick or similar
    magick convert $source -resize ${size}x${size} icon${size}.png
}
```

Or use any image editing software to create simple colored circles:
- Active: `#22C55E` (green)
- Inactive: `#6B7280` (gray)
- Background: Transparent or white

## Notes

- Chrome Web Store requires 128x128 icon
- Toolbar icons are typically 16x16 or 32x32
- Use PNG format with transparency for best results
