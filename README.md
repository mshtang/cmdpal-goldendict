# CmdPal GoldenDict

[简体中文](README.zh-CN.md) · [Releases](https://github.com/mshtang/cmdpal-goldendict/releases) · [Report a bug](https://github.com/mshtang/cmdpal-goldendict/issues)

Offline MDX headword suggestions for **PowerToys Command Palette**, with definitions opened in **GoldenDict-ng**.

Type a word, choose a suggestion, and press Enter. GoldenDict-ng renders the dictionary entry with its own styles and resources. The extension builds its own headword index instead of depending on GoldenDict-ng's private indexes.

**Current release: 2.2.0 development installer. Windows Developer Mode is required.** The installer checks this setting and does not change it. A production-signed installer is not available yet.

## Install

1. Install Command Palette and GoldenDict-ng, and configure your dictionaries in GoldenDict-ng.
2. On Windows 11 x64, download [CmdPal-GoldenDict-2.2.0-dev-Setup-x64.exe](https://github.com/mshtang/cmdpal-goldendict/releases/download/v2.2.0/CmdPal-GoldenDict-2.2.0-dev-Setup-x64.exe).
3. With Windows Developer Mode already enabled, double-click the installer and complete the wizard. The development installer is unsigned; Windows may show an unknown-publisher or SmartScreen prompt.
4. Run **Reload** in Command Palette, then open **Local Dictionary Settings**.
5. Paste your GoldenDict-ng executable path and add the MDX dictionaries used for suggestions. Save.

The installer includes the .NET runtime and installs for the current user. It does not include GoldenDict-ng or dictionary files. English and Simplified Chinese installation/UI languages are supported.

## Search

Open **Local Dictionary** in Palette:

| Input | Result |
| --- | --- |
| `apple` | Exact matches first |
| `appl` | Prefix suggestions such as `apple` and `application` |
| `aple` / `appel` | Spelling corrections, including adjacent letter swaps |
| **Look up:** action | Sends the original input directly to GoldenDict-ng |

Identical headwords from multiple dictionaries are merged, with source dictionary names shown below each result. The group filter selects which dictionaries supply suggestions. The optional GoldenDict group field selects an existing group for definitions; blank preserves GoldenDict-ng's current group.

Use Palette's own command settings to assign an alias or global shortcut. The extension does not register a separate system shortcut.

## Settings

The compact settings page lives inside Palette. Manage program paths, dictionary paths/names/enabled state, groups, suggestion limits, spelling corrections, accent matching, and typing delay.

Paste paths as plain text. Spaces, Unicode, `&`, `#`, square brackets, `/` and `\`, and surrounding quotes are handled without JSON escaping. Saving updates configuration; it does not scan or pre-check dictionary files. Add/remove operations take effect after **Save**.

General settings and dictionary groups use collapsible sections. Click a section header to expand or collapse it. Newly added groups open automatically.

The default group can be **All**, which merges suggestions from enabled dictionaries across all groups. Selecting All leaves GoldenDict-ng's current group unchanged when opening a definition. In `config.json`, use `"defaultGroup": ""` for All, or a configured group name.

**Add group** appears below the last group, with a single **Save** button at the bottom. **Reload** appears only after an external edit conflict; it discards the current draft and reads the configuration file again. Command Palette hosts extension settings inside one shared card and sends form inputs when submitted, so this embedded page uses manual saving.

The interface follows the Windows display language: Simplified Chinese for Chinese display languages and English otherwise. User-defined names, paths, and dictionary content remain unchanged.

Configuration: `%LOCALAPPDATA%\PaletteDictionary\config.json`. Headword cache: the adjacent `cache` directory. `config.example.json` documents the schema. Legacy `uiLanguage` values remain readable; the extension follows the system language and writes `auto` when saving. Saves are atomic, preserve unknown fields, keep a `.bak`, and detect external edit conflicts.

## Upgrade and uninstall

Run a newer installer to upgrade, then reload Palette. The installer rejects downgrades. Remove **CmdPal GoldenDict (Development installer)** from Windows installed apps to uninstall; this unregisters the extension and removes installed program files. Configuration and headword caches remain in the location above. GoldenDict-ng and the original dictionary files are unchanged.

Updates are downloaded manually from Releases. No online lookup or background updater is included.

For installation errors, inspect `%LOCALAPPDATA%\CmdPalGoldenDict\Logs\installer.log` and attach the relevant error when reporting a bug. If migrating from a source/development registration, the installer registers its own installed copy while preserving the existing configuration.

## Scope and performance

- MDX 1.x/2.x headword sections, including UTF-8/UTF-16/GBK/Big5, raw/zlib/LZO blocks, and `Encrypted=0/2`.
- Per-dictionary binary caches with source stamps and checksums; immutable in-memory indexes built in the background.
- Prefix lookup by sorted arrays; bounded spelling corrections through a compact trie with cancellation.
- MDD, CSS/JS, definitions, and rendering stay with GoldenDict-ng.
- No networking, pronunciation, translation, or cross-application text selection features.

Password/device-locked dictionaries are unsupported. Matching uses normalized UTF-16 text and can differ from GoldenDict-ng's own suggestions. See [validation and measured limits](VALIDATION.md).

## Build and contribute

Requires Windows 11 and .NET SDK 10:

```powershell
dotnet build PaletteDictionary.sln -c Release -warnaserror
.\scripts\Test.ps1
.\scripts\Build.ps1
```

Build the EXE using Inno Setup 6/7:

```powershell
.\scripts\Build-Installer.ps1 -IsccPath "C:\Tools\Inno Setup\ISCC.exe"
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for synthetic integration tests, installer tests, portable compiler setup, architecture support, and the GitHub Actions release process. Local configuration, dictionary files, and caches are excluded from the public repository and installer.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE), [third-party notices](THIRD-PARTY-NOTICES.md), and `licenses/`. This is an independent community project, not affiliated with Microsoft or GoldenDict-ng. No dictionaries are distributed.
