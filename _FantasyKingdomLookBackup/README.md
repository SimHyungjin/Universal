# Fantasy Kingdom Look Backup

This folder preserves the lightweight rendering/look settings from the Fantasy Kingdom Unity 6 URP asset before removing the large content package.

Keep this folder outside `Assets` while the original `Assets/URP` folder still exists. It includes `.meta` files, so placing both copies under `Assets` at the same time can create duplicate Unity GUIDs.

## Preserved Files

- `URP/`: original URP pipeline assets, renderer assets, global settings, default volume, and volume profiles.
- `Fog/Materials`: small fog materials referenced by the URP renderer features.
- `Fog/Shaders`: small shadergraph/HLSL/controller files used by the fog materials.
- `QualitySettings.asset`: project quality levels that point to the Fantasy Kingdom URP assets.
- `GraphicsSettings.asset`: project graphics/render pipeline references.
- `ProjectSettings.asset`: project-level rendering-related defaults.

## Main Look Recipe

Scene render settings from the demo:

- Fog: disabled in `RenderSettings`, but fog-like rendering is handled through renderer features such as `HeightFog` and `CubeMapFog`.
- Ambient sky color: `{ r: 0.212, g: 0.227, b: 0.259 }`
- Ambient equator color: `{ r: 0.114, g: 0.125, b: 0.133 }`
- Ambient ground color: `{ r: 0.047, g: 0.043, b: 0.035 }`
- Ambient intensity: `1`
- Reflection resolution: `128`
- Reflection intensity: `1`

Directional Light from the demo:

- Rotation hint: `{ x: 8, y: -68, z: -17 }`
- Color: white
- Intensity: `35`
- Color temperature: `4000K`
- Use color temperature: enabled
- Shadows: soft shadows, strength `1`
- Shadow bias: `0.086`
- Normal bias: `0.05`
- Lightmapping mode: mixed

Recommended high-quality volume profile:

- `URP/VolumeProfiles/Global Volume Profile_Level2.asset`
- Components: Color Curves, Tonemapping, Vignette, Probe Volumes Options, Bloom, White Balance, Color Adjustments
- White Balance: temperature `10`, tint `0`
- Color Adjustments: post exposure `-0.8`
- Bloom settings in profile: threshold `1.3`, intensity `5`, scatter `1`, clamp `1.1`, warm tint `{ r: 1, g: 0.8291936, b: 0.74258757 }`

## URP Quality Pattern

The asset uses separate URP assets per hardware tier:

- `Mobile Low`: render scale `0.5`, depth/opaque textures disabled, terrain holes disabled, main light shadows `2048`, shadow distance `1000`, no soft shadows, dynamic batching enabled.
- `Mobile Medium`: render scale `0.5`, main light shadows `4096`, extra mobile renderer features compared to Low.
- `Mobile High`: render scale `0.7`, main light shadows `4096`, SSAO/fog renderer features.
- `Desktop Low`: render scale `0.65`, main light shadows `4096`, shadow distance `2000`.
- `Desktop` / `Desktop Default`: render scale `0.8`, main light shadows `8192`, shadow distance `2000`, 4 cascades, soft shadow quality high.

Renderer feature pattern:

- Desktop/High/Medium renderers use custom fog features plus SSAO.
- Mobile Low keeps the renderer simpler, with fog features but no full SSAO stack.
- Rendering mode is deferred on the full Desktop renderer.
- Fog renderer features reference the preserved fog materials and shadergraphs. The very large HDRI skybox EXR files were not copied to keep this backup small.

## Restore Workflow After Deleting The Big Asset

1. Delete the large Fantasy Kingdom content folders, but keep `_FantasyKingdomLookBackup`.
2. Copy `_FantasyKingdomLookBackup/URP` back into `Assets/URP`.
3. Copy `_FantasyKingdomLookBackup/Fog/Materials` and `_FantasyKingdomLookBackup/Fog/Shaders` back somewhere under `Assets`, keeping their `.meta` files with them.
4. In Unity, let the editor reimport the restored assets.
5. Assign the desired URP asset in `Project Settings > Graphics` and `Project Settings > Quality`.
6. In your scene, create or select a global `Volume` and assign one of the restored profiles, usually `Global Volume Profile_Level2` for the prettiest desktop look.
7. Recreate the directional light settings above.
8. Re-bake lighting for your own scene. Do not expect the Fantasy Kingdom lightmaps to transfer meaningfully to a different scene.

## Notes

The copied URP renderer assets may reference package shaders/features that must still exist in the project. If Unity reports missing scripts on renderer features, remove those missing renderer features or reinstall the package that provided them.
