# Security and Source Transparency

Texture Unifier is a Cities: Skylines II code mod. This note is meant to make public review easier before installing a release build.

## Runtime Behavior

- The mod does not contain network client/server code, telemetry, analytics, or web requests.
- The mod does not use native interop (`DllImport`), unsafe code, registry access, dynamic assembly loading, or command-line shell execution.
- The mod reads and writes its config, texture packs, import inbox, and preview HTML under the Cities: Skylines II persistent data folder: `ModsData\TextureUnifier`.
- The texture loader reads `.png`, `.jpg`, and `.jpeg` files referenced by the config or selected texture pack.
- The import workflow can copy a user-selected local image file into the active texture pack.
- The only `Process.Start` use is the Options-page button that opens a file or folder through the operating system shell. It passes a path directly and does not build or run command strings.

## Build and Publish Scripts

- `build.ps1` runs `dotnet build` and, for packages, the official Cities: Skylines II `ModPostProcessor`.
- Build and publishing PowerShell scripts are developer helpers only. They are not loaded or executed by the in-game mod runtime.
- Cleanup in `build.ps1` is scoped with parent-path checks before recursive deletion.
- Generated artifacts such as `bin/`, `obj/`, `dist/`, platform binaries, and release zips are excluded from the source repository by `.gitignore`.

## Source Review Notes

- The `src/` tree contains no `ChatGPT`, `Copilot`, AI-assistant, or generated-by comments.
- The `src/` tree is intentionally light on comments; behavior should be inspected through the C# code itself.
- Release packages include a `source` folder so players can compare the shipped source with the public repository while Paradox Mods review status is pending.

Security reports and suspicious behavior reports should be opened as GitHub issues with the Texture Unifier version, install source, and relevant log lines from `Logs\TextureUnifier.Mod.log`.
