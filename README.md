# Save Sync

A free code mod for Software Inc. It adds new content from data mods to an existing save. It only adds: it never removes anything, and your original save file is left untouched because the result goes to a new file.

All code here was written from scratch against the game's public mod API. It contains no game code.

## Status

Test build: tested on small test mods and on one real save with three large mods. Copy your saves before using it.

## What it adds

- New features in existing software types
- New categories
- New software types
- New company types, and new software for existing company types
- Updated popularity, unlock years and submarkets of existing categories and features
- Add-ons for existing software types
- Renamed submarkets and changed dependencies of main features, when their count stays the same

The window opens by itself only for mods already used in the save. To add a mod the save has never used, press "Check all installed mods" in the mod's options.

## What it leaves alone

- Other changed numbers of existing features and types: reported, not applied
- Numbers of a type changed by two mods used in the save: the save keeps no load order, so you choose whose numbers to use, or keep the ones in the save
- Removed or renamed content: the old content stays in the save
- Hardware and new tech specializations: not supported yet
- A new main feature whose specialization the type already uses under another name: skipped, its sub features go under the existing one

It does nothing in multiplayer, in saves from multiplayer, and in the editor.

## Development

`tools/check.ps1` checks the sandbox rules and compiles the mod against the installed game. `tools/install.ps1` copies the mod to `DLLMods/SaveSync`. `tools/build-release.ps1` builds the folder for the Workshop. `tools/use-testmod.ps1` switches between versions of the test data mods in `testmods`.
