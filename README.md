# Phantasy Star Universe Archive Explorer

**PSU Archive Explorer** for *Phantasy Star Universe*.

_(Forked from Tenora Works PSU Generic Parser)_

## What's Included

- **PSU Archive Explorer**: A general-purpose research and editing tool focused on individual files.
- **AdxDecoder**: Automatic decoding of ADX audio files to WAV format.
- **DAT2WAV**: Automatic header removal and conversion of DAT sound files to WAV format.
- **PSULib**: Core DLL containing all file format classes.

## Whats Changed

New Features
- Now exports ADX formats to WAV
- Now exports DAT sound formats to WAV
- Now reads/extracts archives which are identified as ADX instead of being read as null
- Now analyzes ADX mappings file to determine the actual name of certain hashed ADX files
- Now provides hints which will display if a file cannot be open and provides possible resolution
- PSULib now supports raw byte reads
  
Bug Fixes
- Fixes OutOfMemoryException error (Now allows opening/exporting of large files)
- Fixes Application Not Responding when clicking large files in the tree view
- Fixes export from folder to now correctly extract all hashed files
- Fixes application and dialogue boxes not launching center screen
- Fixes application crash when clicking in the .rel tree viewer box
- Fixes application from not responding when moving the window on batch

Updates
- .NET framework moved to 4.8
- C# language moved to 12.0

## How to Use the PSU Archive Explorer

Phantasy Star Universe stores most game data inside the **DATA** folder.  
Mods you create should be placed in the **Addon** folder and they will automatically load when the game launches.

1. Open the tool → **File → Open** → select a hashed file from the DATA folder.
2. The parser will automatically decrypt the file (if supported) and display its contents.

### Common File Extensions & Their Purpose

| Extension | Description |
|-----------|:------------|
| **ADX**   | Sound files (music, sound effects, voice acting) |
| **BIN**   | Text files and scripts (dialogue, menus, item descriptions, mission/enemy scripts). Often paired with **.k** files. |
| **DAT**   | General binary containers/archives (can contain almost anything) |
| **REL**   | Object and mission layout files (places enemies, props, triggers, etc.) |
| **K**     | Text/string files (alternative to .bin for localization and UI strings) |
| **NOM**   | Animation files (mainly player character animations) |
| **SFD**   | Video/cutscene files (often paired with ADX audio) |
| **PSO**   | Compiled Pixel Shader Object |
| **VSO**   | Compiled Vertex Shader Object |
| **XNA**   | Bone/skeleton name files |
| **XNCP**  | UI layout and control files (HUD, menus, interfaces) |
| **XNJ**   | 3D model files |
| **XNM**   | Model material and mesh data (usually paired with XNJ) |
| **XNR**   | Parameter/data table files (item stats, weapons, enemy drops, skills, technics, AI, etc.) |
| **XNT**   | Texture list / material mapping files (links texture slots in models to actual XVR textures) |
| **XVR**   | Texture files (characters, environments, UI, etc.) |

**Note**: To **replace** audio:
- Convert your `.wav` to ADX using **ADX Converter & Player** (available on GameBanana: https://gamebanana.com/tools/6491).
- Rename the resulting `.adx` to match the original hashed filename (remove the `.adx` extension).

Some audio may also be embedded inside `.dat` files and can sometimes be previewed by importing raw data into Audacity.

## Special Thanks
- **essen** — Initial research and [gasetools](https://github.com/essen/gasetools)
- **scriptkiddie** — Heavy research into PSU data formats
- **Agrathejagged** — Tenora Works, documentation, and the general footing for all things modding for PSU
- **VGStream Team** - KC for PSU AOTI

## Included Third-Party Code
- GIMSharp from [Puyo Tools](https://github.com/nickworonekin/puyotools)
- [WpfHexEditorControl](https://github.com/abbaye/WpfHexEditorControl)
- PSU Generic Parser & PSULib from (https://github.com/Agrathejagged/tenora-works)
---

