# GTViewer

Research tool to decode and view Gran Turismo 5 / Gran Turismo 6 (PS3) models.
It reads files from an already extracted game volume; it ships no game data.

## Layout

| Path | What |
|---|---|
| `src/GTFormats` | File format readers (MDL3, TXS3, ...), no UI |
| `src/GTViewer` | Windows viewer (WinForms + Direct3D 11) |
| `tools/gtdev` | Console harness: surveys and checks over all game files |
| `docs/` | Format notes, verified findings only |
| `ROADMAP.md` | What is being worked on now and next |

## Build

.NET 9 SDK, `dotnet build GTViewer.sln`.

## License

GPL-3.0, see `LICENSE`. Third-party references in `THIRD-PARTY-NOTICES.md`.
Gran Turismo is a trademark of Sony Interactive Entertainment; this project is not affiliated with it.
