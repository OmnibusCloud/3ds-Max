# OutWit Render for 3ds Max — Capabilities & Known Limitations

**Status: public alpha.** This document describes, honestly and specifically, what the
plugin transfers well today, what it transfers approximately, what it does not transfer
yet, and what is planned next. It is the companion to every alpha build: if something you
rely on is listed under *approximate* or *not yet supported*, that is a known boundary,
not a surprise — and if something breaks that is listed under *supported*, we want to
hear about it.

## What the plugin does

The plugin exports your 3ds Max scene into a neutral, renderer-agnostic scene contract
and submits it to the OmnibusCloud render farm, where it is rebuilt and rendered with a
physically-based path tracer (Blender/Cycles). The goal is **faithful look transfer** —
the render should read as the image the artist authored — not bit-exact replication of a
legacy rasterizer. Where 3ds Max shading models are non-physical (Scanline's additive
specular, no-GI shading, screen-space environments), the pipeline emulates their *look*
with documented, scene-independent rules.

### How we validate

Every release is verified against a corpus of classic 3ds Max sample scenes — characters
with skinned animation, raytraced glass and chrome, displacement, vertex-color lighting,
motion blur, HDRI and procedural environments — rendered natively in 3ds Max and through
the pipeline from the same camera at the same frame, and compared side by side. On top of
the corpus we run *blind tests*: scenes the pipeline has never seen, exported and
rendered with no scene-specific adjustments, to measure how well the transfer
generalizes. Fixes are always systemic (semantics of a feature), never per-scene tweaks.

## Supported today

### Geometry
- Meshes with full smoothing-group fidelity, including vertices split across smoothing
  groups (hard/soft edge boundaries).
- UV channels, including authored map tiling and offsets — taken from the map's actual
  UV transform matrix, so fractional tilings land on the same repeat phases as in Max.
- Multi/Sub-Object materials and per-face material assignment.
- Displacement maps with Max's surface-relative height semantics.
- Backface-culling semantics of the Scanline renderer (one-sided geometry renders as
  authored; enclosed detail objects stay opaque).
- Objects without materials render with their wireframe color, as in the viewport.

### Animation
- Frame-exact timeline on the scene's native frame numbering and frame rate.
- Object transforms with keyframes; instant-cut (teleport) keys hold correctly.
- Skinned characters (Skin/Physique, Biped, imported FBX rigs): deformation is baked
  per frame in world space, so bone-chain scaling quirks cannot drift the mesh.
- Vertex-level deformation (morphs, Flex, space warps) via sampled deformation frames
  with smooth cross-fading.
- Animated camera and light parameters (position, intensity, color).

### Cameras
- Authored render cameras, frame-exact framing and FOV, including camera cuts.
- Scenes with no camera fall back to the active viewport view.
- Authored render resolution and aspect.

### Lights
- Standard omni / spot / directional lights with their real decay semantics:
  - Decay **None** renders as constant illumination (calibrated against native renders
    with a photometric probe), including lights that also carry attenuation ranges —
    subjects inside the plateau receive full intensity, as in Max.
  - Decay Inverse Square keeps the physical model.
- Photometric lights with intensity normalization.
- Area lights, shadow on/off flags, light color and multiplier animation.
- The Physical Exposure Control's global EV is honored — raising EV in Max darkens the
  farm render the same way, giving artists a familiar brightness knob.

### Materials
- **Standard / Blinn (Scanline-era)**: diffuse color and maps, specular level and
  glossiness, self-illumination — both the spinner and per-pixel self-illumination maps
  (the map replaces the spinner pointwise), including vertex-color-driven emission.
  Specular levels above 100% (a non-physical additive blowout in Scanline) are emulated
  perceptually as camera-only partial self-illumination, so signature looks like blown
  eye highlights survive.
- **Physical Material / Arnold Standard Surface (core parameters)**: base color,
  roughness (including the inverted-spinner mode), metalness, transmission with tint,
  authored IOR, dominant-subsurface handling, normal/bump with authored amounts.
- **Raytrace material**: the color-valued transparency filter (per-material gradations
  from dark translucent to opaque), authored IOR, and mirror reflectivity taken from the
  authored reflect color — white chrome renders as white chrome, tinted chrome keeps its
  tint.
- **Texture pipeline**: bitmap textures in base color / bump / normal / roughness /
  metalness / opacity / displacement slots; simple procedural maps (Gradient, Noise,
  Checker, Mix and similar) are baked to images at export so the authored pattern
  survives.

### Environments & backgrounds
- Background color, bitmap environments, HDRI environments with automatic exposure
  normalization.
- Procedural environments are baked at export.
- **Screen-mapped environments** (backdrops stretched across the render window — a very
  common Scanline setup) render correctly for the camera, and are also visible in
  mirrors and glass.

### Renderer emulation profiles
- **Default Scanline**: no global illumination (self-illuminated surfaces glow without
  lighting the scene), additive-specular semantics, Standard view transform, screen
  backdrops, image motion blur as a post-process vector blur — matching Scanline's
  "sharp frame with smear on top" model.
- **Physically-based scenes** (Arnold/Physical materials, HDRI lighting) render with
  full path tracing.

### Motion blur
- Image motion blur (per-object flag) → compositor vector blur.
- Object motion blur → real shutter integration.
- The blur kind is detected per scene from the objects' authored flags.

### Farm pipeline
- Distributed still and video (H.264) rendering, resolution/samples/denoise controls,
  automatic texture/asset upload, job caching.

## Approximate / best-effort today

These transfer with documented approximations. Expect a faithful read, not an exact one.

- **Complex procedural material graphs** — multi-level Mix/mask trees driving
  self-illumination or displacement (showcase shaders like lava slates) do not survive:
  simple leaves are baked, but deep graph logic is not translated yet. This is the
  top-priority v2 item.
- **V-Ray materials** — dedicated support, approximate by design:
  - **VRayMtl** maps to the neutral PBR surface: diffuse color and texture, reflection
    glossiness/metalness/Fresnel IOR, refraction (with fog tint and glossiness),
    self-illumination, and direct bitmap slots (including V-Ray Bitmap files and normal
    maps). Procedural V-Ray maps are not baked.
  - **VRayScannedMtl** carries a measured BRDF that cannot be reconstructed; the exporter
    approximates it from the paint/filter overrides and the scan's own type and color
    naming (a red car-paint scan renders as glossy red metallic paint, not as its exact
    measured response).
  - V-Ray **lights, sun/sky environment, and Physical Camera exposure** are not mapped
    yet (a neutral light stands in) — this is the current work-in-progress wave, so
    V-Ray scenes render with faithful geometry/camera/materials under neutral lighting.
- **Other third-party renderer materials (Corona, Octane, Redshift, …)** — *untested*.
  The exporter takes a minimal safe read (viewport diffuse), so basic color may come
  through, but no fidelity is promised. Scenes authored for Scanline, Arnold, or
  Physical Material remain the fully supported path.
- **Far-attenuation fade windows** — the constant plateau is modeled; the linear fade
  zone between Start and End is not (subjects inside the fade zone render somewhat
  brighter than native).
- **Authored UV rotation on maps** — not transferred (the map renders untransformed
  rather than mis-rotated).
- **Per-material environment/reflection map slots** — skipped as albedo sources;
  reflective materials reflect the scene's world backdrop instead, which usually matches
  the authored intent (the same image is typically assigned in both places).
- **Baked procedural skies** — the cloud contrast of 3D noise-based skies bakes softer
  than the native render.
- **Ambient light** — the scene ambient color is not yet transferred; deeply shadowed
  areas can read slightly darker than native.

## Not yet supported

- Full procedural material graph translation (see above — planned).
- Volumetrics and atmospherics: fog, volume lights, environment effects.
- Render effects and post effects: lens effects, glow, film grain.
- Particle systems, hair/fur, and cloth beyond what is already baked into renderable
  meshes at export time.
- Scripted/plugin materials and DirectX/viewport shaders.
- Radiosity-baked lighting solutions.
- IES photometric web profiles (intensity transfers; the web pattern does not).

## Planned next (v2 direction, feedback-driven)

1. **Procedural material graph transfer** — translating or lit-baking deep map trees so
   showcase shaders survive.
2. **V-Ray lighting and camera** — VRayLight/VRaySun/VRaySky environment and Physical
   Camera exposure mapping (materials shipped; Corona and other renderers to follow).
3. **Attenuation fade windows and UV rotation** — closing the two known approximation
   gaps.
4. **Ambient light transfer and sky bake fidelity.**
5. **In-Max UI/UX overhaul** — the current alpha UI is minimal; a proper submission,
   monitoring, and settings experience is in progress.
6. Whatever the feedback ranks higher than the list above.

## Reporting feedback

Please open an issue on the GitHub repository with:
- the plugin version (Help → About) and, if known, the server version,
- a minimal scene (or a description of the material/light/feature involved),
- the native render and the farm render, if you can share them.

Scenes that render *differently* are exactly the input this alpha needs — the corpus
above grew out of reports like yours.
