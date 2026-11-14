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

`Build.ps1` publishes only the extension, using self-contained full trimming by default. It stages into a new directory, checks the payload, then replaces an empty or recognized previous publish directory, removing stale CLI/runtime files. Do not store personal files in publish directories; dictionaries, local configurations and caches cause replacement to be rejected. PDB files are omitted from the payload. Runtime diagnostic native libraries remain included. The CLI remains available through `dotnet run --project src/PaletteDictionary.Cli -- <arguments>` or a separate `dotnet publish src/PaletteDictionary.Cli -c Release -r win-x64 --self-contained true -o artifacts/cli`.

The extension and smoke client pin Windows SDK managed projections to `10.0.26100.87`, whose generated ABI annotations resolve the older `.57` projection's IL2081 warnings. CsWinRT runs in `Auto` mode to generate WinRT vtables for extension classes. Settings construct JSON through `JsonNode` overloads instead of reflection-based `JsonArray.Add<T>`. No global trim warning suppression is used; trim warnings fail publishing. See [C#/WinRT trimming guidance](https://github.com/microsoft/CsWinRT/blob/master/docs/aot-trimming.md).

`Build.ps1 -FrameworkDependent -OutputDirectory artifacts/framework-dependent` remains available for source development and requires a matching architecture's shared .NET 10 runtime. It disables trimming. Command Palette's private runtime is not a shared dependency. `Build-Installer.ps1`, including `-SkipBuild`, rejects this payload because the installer promises self-contained operation.

Validate the actual trimmed publication:

```powershell
.\scripts\Test-Trimmed.ps1
```

This publishes and runs the core suite with full trimming (including MDX decoding and error recovery), then exercises the installer payload over cross-process COM with 25 synthetic dictionaries: MDX 1.x/2.x × UTF-8/UTF-16/GBK/Big5 × raw/LZO/zlib, plus Encrypted=2. Each dictionary has a unique searched headword. Only the extension child process is pointed at an empty shared-runtime directory. Settings, conflict recovery, group filtering and shutdown are also exercised. CI runs this after building the installer. `Test-Publish.ps1` rejects CLI/PDB/user data and enforces a 35 MiB self-contained payload budget and a 1 MiB managed Windows SDK projection budget.

Cross-process tests use synthetic MDX headwords; no third-party dictionary is required:

```powershell
.\tests\PaletteDictionary.Tests\bin\Release\net10.0\PaletteDictionary.Tests.exe --write-smoke-config .\artifacts\smoke\config.json
.\tests\PaletteDictionary.Smoke\bin\Release\net10.0-windows10.0.26100.0\PaletteDictionary.Smoke.exe .\artifacts\installer-payload\win-x64\PaletteDictionary.Extension.exe .\artifacts\smoke\config.json en
```

On a machine where Developer Mode is already enabled, `scripts/Test-Installer.ps1 -Installer <setup.exe>` exercises installation, reinstallation, activation via the real registered CLSID, failed relocation rollback, downgrade rejection, ownership-safe unregistration, and uninstall. It preserves user configuration/cache and restores a previous development registration. It intentionally does not change Developer Mode. Same-version development registrations at a different path are removed with application data preserved before registration at the new path; a failed registration restores the previous manifest.

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
