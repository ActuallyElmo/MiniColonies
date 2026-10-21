# Pixel Water

Assign `PixelWaterMat` to the horizontal water plane. The shader is
`MiniColonies/Pixel Water`; edit `PixelWater.shader` directly.

The surface uses a fixed world-space pixel grid, curved traveling ripples,
three ripple tones, broken crest highlights, and animated shoreline wash.
The material carries the matching palette and effect defaults.

## Building reflections

Enable **Reflect Visible Scene Objects**. The active URP asset must enable
**Depth Texture** and **Opaque Texture** (both are already enabled in
`PC_RPAsset`). The reflection samples the camera's opaque color and depth.
It includes the building shader's existing vertex bend and two-tone colors.

This is a stylized screen-space reprojection for the straight-down camera.
An ordinary horizontal mirror would put much of a building's reflection
under its footprint. This shader deliberately reverses and extends its
apparent lean onto the water. Off-screen objects, hidden faces, and
transparent objects are unavailable; reflections fade at screen edges.
If camera textures are unavailable, the water uses its environment reflection.

- **Reflection Ray Direction** controls the direction and length of the
  reflection. For example, `TestHouseOrange` bends by `(-0.105, +0.105)`
  per unit of height. `(-0.21, +0.21)` gives its matching reverse lean.
  The preset `(-0.4, +0.4)` extends it farther for readability.
- **Reflection Height Reach** is the height above water searched for objects.
  Keep it close to the height of nearby buildings: a needlessly large value
  makes the 24 search steps coarser and can miss small features.
- **Object Reflection Strength** controls reflected color visibility.
- **Reflection Ripple Distortion** bends the reflected image with the waves.

Only buildings on the appropriate side of the water reflect into it, according
to the selected reflection direction. Buildings farther inland than their
reflected height reaches will not appear in the water.

## Tuning and smoke check

- Increase **Pixel Size** for chunkier pixels.
- Increase **Wave Length** for broader ripples; **Flow Speed** moves them.
- Adjust **Ripple Color Contrast** and **Wave Crest Strength** for quieter or
  more graphic water. **Scene Lighting Influence** lets the palette stay bright.
- **Foam Width** and **Shore Wave Speed** control the waterline.

In Play Mode, place a tall, brightly colored building beside a shore where the
reflection extends onto water. Check its reflected colors and reversed lean,
then pan and zoom to check alignment and the screen-edge fade. Toggle
**Reflect Visible Scene Objects** to compare. Check a shore without buildings,
and stop Play Mode to check for shader errors in the Console.

The reflection search is bounded to 24 steps and at most two five-step
refinements. Disable scene reflections on hardware where that GPU cost is
unwanted. No additional cameras or per-frame C# work are used.
