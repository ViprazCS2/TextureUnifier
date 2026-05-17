# Texture Unifier for Cities: Skylines II

Texture Unifier is a small code mod that keeps terrain, road, gravel, and advanced sidewalk texture swaps in one config file.

It targets the texture paths you were already using for terrain and extends the same idea to network materials by scanning loaded Unity materials. Terrain replacement is exact because CS2 exposes those terrain textures as shader globals. Road and sidewalk replacement is best-effort because those are normal Unity materials, so the config exposes match keywords you can tune if a material is missed.

## Security and Source Transparency

Texture Unifier does not contain network code, telemetry, native interop, registry access, unsafe code, dynamic assembly loading, or command-string shell execution.

Runtime file access is limited to the CS2 persistent data area used by the mod, plus texture files the user chooses or references in `config.json`. The Options-page folder buttons use one `Process.Start` call to open a path through the operating system shell.

PowerShell files in this repository are developer build/release helpers around the official CS2 modding toolchain. They are not runtime mod code and are not executed by subscribers in-game.

See [SECURITY.md](SECURITY.md) for the public review notes.

## Build

```powershell
cd path\to\TextureUnifier
.\build.ps1
```

The build script auto-detects the Steam install from `libraryfolders.vdf`. If needed, pass a game path manually:

```powershell
.\build.ps1 -GamePath "E:\SteamLibrary\steamapps\common\Cities Skylines II"
```

The built DLL lands here:

```text
bin\Release\TextureUnifier.dll
```

To copy the DLL into CS2's local `Mods` folder and create the `ModsData\TextureUnifier\Textures` folder:

```powershell
.\build.ps1 -Deploy
```

Then fully restart Cities: Skylines II with code mods enabled. Local development mods live outside the Paradox subscription playset, so they may not show up like subscribed mods.

To create a shareable manual-install zip:

```powershell
.\build.ps1 -Package
```

Packaging runs the official CS2 mod postprocessor and includes the generated Windows, macOS, and Linux files needed by the Paradox Mods publisher.

The release zip lands in:

```text
dist
```

The zip contains a `TextureUnifier` folder with the DLL, README, and sample config. For manual installs, copy or unzip that folder into:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods
```

## In-Game Interface

Texture Unifier registers a native Cities: Skylines II Options page named `Texture Unifier`.

The Options page exposes the release-safe controls:

- master enable switch
- `New texture folder`
- `Open texture folder`
- close-up and far-away terrain texture scale
- advanced surface-strength sliders for normal maps
- a `Roads & More` tab for advanced road, sidewalk, and gravel controls
- a `Grass` tab with enable, scale, and spacing

The old import slots, exact file path fields, per-slot enable toggles, rescan timing, and diagnostic controls are intentionally hidden from normal users. Texture paths, shader property lists, and matching keywords remain in `config.json` for texture authors who need them.

## Texture Packs

Texture Unifier does not ship with copyrighted or paid textures. Users add their own textures to the folder created from the Options page.

The fastest path:

1. Open Options -> Texture Unifier.
2. Press `New texture folder`.
3. Put `.png`, `.jpg`, or `.jpeg` files into the folder that opens. Standard pack files can use the same base name with any of those extensions, for example `Road_BaseColor.png`, `Road_BaseColor.jpg`, or `Road_BaseColor.jpeg`.
4. Use the required file names below.

`*_BaseColor` files are the colorful textures. Normal files (the pink textures) are only for bump/surface detail; they are not color textures.

Replacing a file in the active texture folder is enough. The mod reloads changed texture files during its normal terrain and material passes.

Texture packs live here:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\TextureUnifier\Packs
```

Each pack is just a folder:

```text
Packs\MyTexturePack\Grass_BaseColor.png
Packs\MyTexturePack\Grass_Normal.png
Packs\MyTexturePack\Dirt_BaseColor.png
Packs\MyTexturePack\Dirt_Normal.png
Packs\MyTexturePack\Cliff_BaseColor.png
Packs\MyTexturePack\Cliff_Normal.png
Packs\MyTexturePack\Road_BaseColor.png
Packs\MyTexturePack\Road_Normal.png
Packs\MyTexturePack\Sidewalk_BaseColor.png
Packs\MyTexturePack\Sidewalk_Normal.png
Packs\MyTexturePack\Gravel_BaseColor.png
Packs\MyTexturePack\Gravel_Normal.png
```

One-off imports can be dropped here:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\TextureUnifier\ImportInbox
```

Optional road-wear files:

```text
Packs\MyTexturePack\RoadWear_BaseColor.png
Packs\MyTexturePack\RoadWear_Normal.png
```

When `New texture folder` is pressed, Texture Unifier writes the standard `MyTexturePack` paths into `config.json` and reloads immediately.

Sidewalk replacement is available for users who want it. Turn on advanced options, open `Roads & More`, and enable `Change sidewalks`. CS2 sidewalk materials can use a custom UV layout, so some seamless textures may look stretched or road-like there; use `Sidewalk_BaseColor` and `Sidewalk_Normal` files authored for the result you want.

Vanilla parking-lot road surfaces use the normal road texture files. Texture Unifier intentionally does not expose a separate parking-lot atlas slot because CS2 stretches that UV layout by design.

## Runtime Config

The deploy script creates this folder for you. If you only build the DLL and do not deploy, the mod creates it on first in-game load:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\TextureUnifier
```

Put textures in:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\TextureUnifier\Textures
```

Default expected files:

```text
Grass_BaseColor.png
Grass_Normal.png
Dirt_BaseColor.png
Dirt_Normal.png
Cliff_BaseColor.png
Cliff_Normal.png
Road_BaseColor.png
Road_Normal.png
Sidewalk_BaseColor.png
Sidewalk_Normal.png
Gravel_BaseColor.png
Gravel_Normal.png
```

You can also point the config at `.jpg` files from Bridge, for example:

```json
"baseColor": "Textures/asphalt_rough_ulzmdclew/ulzmdclew_4K_Albedo.jpg",
"normal": "Textures/asphalt_rough_ulzmdclew/ulzmdclew_4K_Normal.jpg"
```

The config auto-reloads every few seconds, so you can adjust file paths and scale values while testing.

Normal maps are bump/surface-detail textures, not colorful textures. Advanced users can amplify them per slot:

```json
"normalStrength": 2.0
```

`1.0` leaves the normal map unchanged. Values around `1.5` to `3.0` are useful when replacement normal maps look too flat in CS2's terrain and network shaders.

## Scale Controls

Terrain uses CS2's global `colossal_TerrainTextureTiling` vector:

```json
"lowFrequencyScale": 80.0,
"highFrequencyScale": 200.0,
"dirtHighFrequencyScale": 240.0
```

Roads, sidewalks, and gravel use material texture tiling:

```json
"lowFrequencyScale": 1.0,
"highFrequencyScale": 1.0
```

`lowFrequencyScale` is applied to the base color and normal maps. `highFrequencyScale` is applied to detail textures when the material has detail slots.
Road-style asset surfaces that do not use CS2's world-space road material automatically use half of the road low-frequency tiling, matching the 0.1.26 asset behavior without repeating the texture twice.

## Matching Roads And Sidewalks

The road and sidewalk matchers both target CS2's `Road_` network material, but they write to different shader properties:

```text
road     -> _WorldspaceAlbedo / _WorldspaceNormalMap
sidewalk -> _BaseColorMap / _NormalMap
```

That matters because `CarLane_BaseColor` is the road wear overlay, not the actual drivable asphalt.

If sidewalks do not change, open:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\TextureUnifier.Mod.log
```

Look for material and texture names, then add a distinctive word to `materialNameKeywords` or `textureNameKeywords` in `config.json`.

## Experimental Foliage Grass

CS2 contains a disabled foliage VFX system for grass. Texture Unifier exposes it as one Options-page switch for normal use: enable Texture Unifier, open the Grass tab, and turn on `Enable grass`.

The mod now starts the game's `VegetationRenderSystem` automatically, so `-developerMode` and the hidden Game Rendering > Terrain > Foliage toggle are not required. When grass is enabled, Texture Unifier uses a conservative profile: generated road/building/track/surface mask, fixed local VFX crop, per-frame refresh from the pre-culling phase, stock scatter disabled, no forced VFX play during map load, locked away-from-sun lighting direction, and no density/render-distance remapping. The default internal grass values are scale `256`, spacing `1536`, road mask resolution `4096`, delayed change-detected mask updates, stable lighting, and diagnostics enabled. Old preset, density, render-distance, AO, particle-budget, foliage blade texture, and foliage color-match fields remain in `config.json` only for backward compatibility; they are normalized back to the simple profile when the file loads. Network material rescans are also throttled and only write material values that actually changed.

The minimal config is:

```json
"foliage": {
  "enabled": true
}
```

The generated mask copies the game's terrain splatmap and paints road edges/nodes, train/tram/subway tracks, building footprints, and placed surface areas black before feeding it to the foliage VFX. Road-mask resolution is clamped to 4096-8192 because lower resolutions can make the stock foliage mask sampling miss carve-outs entirely. The expensive mask is not updated while grass is disabled. When grass is enabled, Texture Unifier checks geometry changes less often, waits several seconds for edits to settle, and spaces scene-triggered mask rebuilds apart to reduce stutter.

Stock foliage grass can pick up an overly bright yellow-green response when the camera direction lines up with the sun. Texture Unifier now keeps the VFX `CameraDirection` pinned to the away-from-sun direction, which uses the visually natural opposite-angle color as the lighting target while the real camera position still drives placement.

The `foliage.texture` block is kept only as legacy config data. The stock foliage VFX graph has not exposed compatible blade texture or tint inputs in testing, so the release profile disables those values instead of showing controls that appear to do nothing.

Packaged releases include a `source` folder with the C# source files for easier inspection while Paradox Mods review status is pending.

Link to the paradox mods mod: https://mods.paradoxplaza.com/mods/142632

CS2 already has plenty of paid content, so Texture Unifier stays focused on free customization using your own textures. If you appreciate that, consider supporting me: buymeacoffee.com/viprazCS2

Hope you enjoy everything :) 
