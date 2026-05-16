# Texture Unifier User Guide

Texture Unifier is a Cities: Skylines II code mod for swapping terrain and network textures from a local texture pack.

The mod does not include texture assets. You provide your own PNG or JPG files.

## What It Can Replace

Normal texture-pack slots:

- grass base color and normal
- dirt base color and normal
- cliff base color and normal
- road base color and normal
- optional sidewalk base color and normal
- gravel base color and normal
- optional road-wear base color and normal

Sidewalk replacement is optional. Turn on advanced options, open `Roads & More`, and enable `Change sidewalks` if you want `Sidewalk_BaseColor` and `Sidewalk_Normal` to apply.
Parking-lot road surfaces use the normal road texture files. There is no separate parking-lot texture file because CS2 stretches that UV layout by design.

## Quick Start

1. Subscribe/install Texture Unifier.
2. Start Cities: Skylines II with code mods enabled.
3. Open `Options > Texture Unifier`.
4. Press `New texture folder`.
5. Put your `.png`, `.jpg`, or `.jpeg` textures into the folder that opens.
6. Use the required file names below.

Texture Unifier uses `MyTexturePack` automatically when it creates the folder.
Standard pack files can use the same base name with `.png`, `.jpg`, or `.jpeg`, such as `Road_BaseColor.png` or `Road_BaseColor.jpg`.

The full data folder is:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\TextureUnifier
```

Colorful textures go in `*_BaseColor` files. Normal files (the pink textures) are only for bump/surface detail, not color.

## Texture Pack Folder

Texture packs live in:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\TextureUnifier\Packs
```

Each pack is a folder:

```text
Packs\MyTexturePack\Road_BaseColor.png
Packs\MyTexturePack\Road_Normal.png
Packs\MyTexturePack\Sidewalk_BaseColor.png
Packs\MyTexturePack\Sidewalk_Normal.png
```

## Required Names

Recommended pack files:

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

Optional road-wear files:

```text
RoadWear_BaseColor.png
RoadWear_Normal.png
```

## Advanced Config

Advanced users can edit:

```text
ModsData\TextureUnifier\config.json
```

This file controls texture paths, surface strength, tiling scale, material matching keywords, and the grass enable and lighting-stabilization flags.

The in-game Options page is safest for common use. Edit `config.json` only when you need custom paths or material matching rules.

## Troubleshooting

If a texture does not appear:

- Confirm the file is in `Packs\MyTexturePack`.
- Confirm the file uses one of the required names exactly.
- Check the mod log:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\TextureUnifier.Mod.log
```

If a normal map looks too flat or too strong, turn on advanced options and adjust the surface strength sliders.

If a road texture scale looks wrong, adjust the network tiling values in `config.json`.

If a sidewalk texture looks wrong, try a sidewalk-specific texture or turn off `Change sidewalks`. Some CS2 sidewalk materials use a different UV layout than road asphalt.
