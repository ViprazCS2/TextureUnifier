# Known Issues

## Foliage Directional Tint

The stock CS2 foliage grass can take on an unnatural yellow-green tint when viewed/lit from the same direction as the sun. The color looks most natural from the opposite sun angle, and that opposite-angle color should be treated as the visual target for future foliage color/lighting work.

Texture Unifier now applies a lighting workaround for this path: it leaves the road/building mask alone, but overwrites the stock foliage VFX `CameraDirection` with the away-from-sun direction after the game updates the effect. Previous tests did not find exposed foliage blade texture or tint properties on the stock VFX graph, so this remains a VFX lighting workaround rather than a material texture replacement.
