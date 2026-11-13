# Contributing

Report bugs with Windows, Command Palette, GoldenDict-ng, and extension versions. Pull requests should describe the resulting behavior and relevant validation.

## Build and test

Windows 11 and .NET SDK 10 are required. Restore uses NuGet; the extension itself is offline.

```powershell
dotnet build PaletteDictionary.sln -c Release -warnaserror
.\scripts\Test.ps1
.\scripts\Build.ps1
```

To build the development EXE installer, install Inno Setup 6/7 or obtain the pinned portable compiler:

```powershell
$compiler = .\scripts\Get-InnoSetup.ps1 -Destination .\artifacts\build-tools
.\scripts\Build-Installer.ps1 -IsccPath $compiler
```

The output is `artifacts/installers/CmdPal-GoldenDict-<version>-dev-Setup-x64.exe` and its SHA256 file. ARM64 builds are available with `-Runtime win-arm64`, but are not included in releases until validated on ARM hardware.

Cross-process tests use synthetic MDX headwords; no third-party dictionary is required:

```powershell
.\tests\PaletteDictionary.Tests\bin\Release\net10.0\PaletteDictionary.Tests.exe --write-smoke-config .\artifacts\smoke\config.json
.\tests\PaletteDictionary.Smoke\bin\Release\net10.0-windows10.0.26100.0\PaletteDictionary.Smoke.exe .\artifacts\installer-payload\win-x64\PaletteDictionary.Extension.exe .\artifacts\smoke\config.json en
```

On a machine where Developer Mode is already enabled, `scripts/Test-Installer.ps1 -Installer <setup.exe>` exercises installation, reinstallation, activation via the real registered CLSID, downgrade rejection, ownership-safe unregistration, and uninstall. It preserves user configuration/cache and restores a previous development registration. It intentionally does not change Developer Mode.

## Project layout

- `src/PaletteDictionary.Core`: MDX headword extraction, caching, indexes, configuration, shared localization.
- `src/PaletteDictionary.Extension`: native Command Palette UI and COM server.
- `src/PaletteDictionary.Cli`: local diagnostic commands.
- `installer`: bilingual Inno Setup wizard and scoped package registration helper.
- `tests`: synthetic fixtures, core tests, and cross-process tests.

## Publishing

Update `Directory.Build.props`, the package manifest version, `CHANGELOG.md`, and `docs/releases/v<version>.md` together. Push the matching `v<version>` tag. The release workflow builds and tests the extension, then publishes the EXE and checksum as a **prerelease**.

Current installers are development builds and require Developer Mode. A future production installer must use a properly signed identity/package and a trusted publisher; do not silently enable development settings or install self-signed trust certificates.

Keep local configuration, dictionary files, caches, credentials, and certificates out of source control and release assets. Contributions are under GPL-3.0-or-later; preserve the existing third-party notices and licenses.
