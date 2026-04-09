# Phantasy Star Universe Generic Parser

**PSULib**, **PSU Generic Parser** for *Phantasy Star Universe*.

## What's Included

- **PSULib**: Core DLL containing all file format classes.
- **PSU Generic Parser** (Resurrected): A general-purpose research and editing tool focused on individual files. It does **not** correlate data across files (e.g., weapon stats and star ratings are edited separately).  

## How to Use the PSU Generic Parser

Phantasy Star Universe stores most game data inside the **DATA** folder.  
Mods you create should be placed in the **Addon** folder — they will automatically load when the game launches.

1. Open the tool → **File > Open** → select a hashed file from the DATA folder.
2. The parser will automatically decrypt the file (if supported) and display its contents.

### Common File Extensions & Their Purpose

| Extension | Description |
|-----------|-------------|
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

**Note**: Some files may appear empty when opened. This is common with **ADX** audio files. Rename the file to end with `.adx` and use **vgmstream-cli** (from [vgmstream.org](https://vgmstream.org/)) to convert it to `.wav` for preview.

To **replace** audio:
- Convert your `.wav` to ADX using **ADX Converter & Player** (available on GameBanana: https://gamebanana.com/tools/6491).
- Rename the resulting `.adx` to match the original hashed filename (remove the `.adx` extension).

Some audio may also be embedded inside `.dat` files and can sometimes be previewed by importing raw data into Audacity.

## Supported Data (PSU Generic Parser)

### Gameplay
- `itemCommonInfo.xnr` (view/edit)
- `itemBulletParam*.xnr`, `itemSkillParam*.xnr`, `itemTechParam*.xnr`, `itemLineUnitParam.xnr`, `itemPartsParam.xnr`, `itemSuitParam.xnr`, `itemWeaponParam*.xnr`
- `itemEnemyDrop.xnr`
- `EnemyLevelBaseParam.xnr`
- `*Tutor.bin` enemy scripts
- `obj_param.xnr`, `obj_particle_info.xnr` (rough support)

### Missions
- `enemy_*.xnr` — Monster layout data
- `set_r*.rel` — Object layout data
- `.bin` mission scripts

### Aesthetic
- **XVR** textures (view/edit)
- **XNT** texture lists (view/edit)
- **NOM** animations (view only, very rough)
- `partsinfo.xnr` — Character appearance mapping

### Miscellaneous
- `.k` / `.bin` text files

## Special Thanks
- **essen** — Initial research and [gasetools](https://github.com/essen/gasetools)
- **scriptkiddie** — Heavy research into PSU data formats
- **Agrathejagged** — FPB Extractor improvements, documentation, and contribution to modding in general for PSU

## Included Third-Party Code
- GIMSharp from [Puyo Tools](https://github.com/nickworonekin/puyotools)
- [WpfHexEditorControl](https://github.com/abbaye/WpfHexEditorControl)

## Whats Changed
- Fixes OutOfMemoryException error (Now allows opening/exporting of large files)
- Application now launches center screen
- Hints are now displayed if a file cannot be opened and provides possible resolution
- .NET framework moved to 4.8

---

