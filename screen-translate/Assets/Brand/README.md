# Screen Translate artwork

Unmodified copies of the approved **Capture Reply** assets from:
`C:\Users\jcbca\Documents\Codex\2026-09-05\bna\outputs\screen-translate-final-icon`

The SVG masters and both ICO variants are retained here. PNG exports cover 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels in both themes. The small exports use the supplied simplified artwork.

`ApplicationIcon` embeds `screen-translate.ico` into the Windows executable. `AppArtwork` loads the embedded default ICO for the main window and the PNGs for the header. The header chooses the matching theme and the smallest export at least as large as its physical pixel size. The main window keeps the default ICO because its title bar and taskbar surfaces are controlled by Windows independently of the content theme. Artwork is disposed with the window.

Light artwork: charcoal `#24343C`, teal `#087F70`, white. Dark artwork: pale gray `#E4EAE9`, teal `#31C4AE`, charcoal `#16242A`.

No artwork was regenerated or recolored. No loose asset files are required at runtime.
