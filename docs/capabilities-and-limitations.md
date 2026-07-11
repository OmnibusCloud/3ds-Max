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
- **Mirrored objects** (the Mirror tool's negative scale): the reflection is folded into
  the exported geometry, so mirrored halves render exactly where Max puts them —
  verified against Max's own world-space ground truth.
- UV channels, including authored map tiling and offsets — taken from the map's actual
  UV transform matrix, so fractional tilings land on the same repeat phases as in Max.
- Multi/Sub-Object materials and per-face material assignment.
- Displacement maps with Max's surface-relative height semantics.
- Backface-culling semantics of the Scanline renderer (one-sided geometry renders as
  authored; enclosed detail objects stay opaque).
- Objects without materials render with their wireframe color, as in the viewport.
- Scene preparation is fast: mesh data is read in bulk and assembled on all CPU cores
  (a scene that took minutes to capture in early alphas exports in seconds), and static
  meshes are never re-sampled per frame.

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
- Robust asset handling: two textures sharing a filename from different folders stay
  distinct; a texture whose source file is missing degrades that one texture with a
  named warning instead of failing the submission (matching Max's own behavior), and
  the degraded scene is re-validated before upload so it can never fail late on the farm.

### Export to Blender (.blend)
- The Export dialog can build the scene **server-side into a `.blend` file** and hand it
  back — all textures (including baked ones) packed inside, so the file is
  self-contained. The same neutral contract drives it, so everything in this document
  applies to the exported file as well.

### Honest diagnostics
- Everything the exporter approximates or cannot carry leaves a **named warning** in the
  export/launch diagnostics: which feature, which objects (by name), and what to do
  about it. A scene with V-Ray proxies, animated visibility, or custom normals tells
  you so up front — silent wrongness is treated as a bug.

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
    measured response). For fabric-like scans (cloth, suede, leather) an opt-in
    **local bake** renders the scan's surface into a texture with your local V-Ray
    before upload, preserving weave and nap detail — the checkbox appears in the Render
    and Export dialogs only when the scene actually carries scanned materials
    (interactive 3ds Max only; specular-dominant scans like car paint keep the
    parametric look, where the bake cannot help; objects without a usable unwrap are
    skipped with a diagnostic). The bake runs under a stripped-down render preset and
    is capped at two minutes per material; your scene's render settings and
    render-to-texture setup are restored afterwards.
  - **V-Ray lights and environment**: VRayLight plane/disc/sphere map to area/point
    lights with their authored colour, multiplier, and physical units; a dome light
    becomes the world environment (its HDRI travels as the world image and suppresses
    the fallback light rig); VRaySun maps to a calibrated sun; a V-Ray Bitmap
    environment ships its HDR file directly (no LDR bake). Colour-correction layers on
    the environment and mesh lights are not translated.
  - **Physical camera exposure** (the stock Physical Camera and VRayPhysicalCamera) —
    the authored exposure carries as a deviation from a photographic reference, and
    only towards darkening: an EV above the reference is an authored mood (sunset,
    night), while a fast lens compensating dim physical light stays neutral — the
    exporter's light calibration already normalizes intensity. VRayLightMtl emissive
    panels map to real emission. V-Ray tone mapping (color mapping curves) and the
    environment intensity multiplier are not translated — the calibrated exposure
    absorbs their overall effect.
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
- **Third-party proxy geometry (VRayProxy, CoronaProxy…)** — exports whatever the
  viewport shows: a proxy in full-mesh display mode transfers that mesh; one in
  box/preview mode transfers the preview. VRayPlane (infinite plane) does not convert
  and is **missing** from the render. Every case is named in the diagnostics.
- **Texture map-channel routing** — maps sample the primary UV channel; a map authored
  on channel 2/3 (lightmap/detail workflows) renders with the wrong coordinates and is
  flagged per material.
- **Explicit (custom) normals** — Edit Normals data is approximated from smoothing
  groups; imported CAD/FBX assets with weighted normals may show shading seams (flagged
  per object).
- **Mirrored cameras and lights** — position and aim survive; the reflection itself
  cannot be represented (flagged; mirrored *geometry* is fully supported).

## Not yet supported

- Full procedural material graph translation (see above — planned).
- Volumetrics and atmospherics: fog, volume lights, environment effects.
- Render effects and post effects: lens effects, glow, film grain.
- Particle systems, hair/fur, and cloth beyond what is already baked into renderable
  meshes at export time.
- Scripted/plugin materials and DirectX/viewport shaders.
- Radiosity-baked lighting solutions.
- IES photometric web profiles (intensity transfers; the web pattern does not).
- **Animated object visibility** — the visibility track is not exported; keyed objects
  stay visible in every frame (flagged per object).
- **XRef scene files** — content referenced through XRef *scenes* is not exported (XRef
  *objects* transfer fine); flagged with the file count.

## Planned next (v2 direction, feedback-driven)

1. **In-Max UI/UX overhaul** — the diagnostics described above already carry names and
   suggested actions; surfacing them in a proper Details view, plus a full submission,
   monitoring, and settings experience, is the current work.
2. **Full proxy geometry** — loading VRayProxy/CoronaProxy meshes instead of their
   viewport previews.
3. **Procedural material graph transfer** — translating or lit-baking deep map trees so
   showcase shaders survive.
4. **V-Ray refinements** — glossiness *maps* (with inversion), tone-mapping curves,
   environment intensity multipliers, colour-correction layers on environments, mesh
   lights (exposure, materials, and lighting shipped; Corona and other renderers to
   follow).
5. **Attenuation fade windows, UV rotation, map-channel routing, explicit normals** —
   closing the known approximation gaps, roughly in feedback order.
6. **Ambient light transfer and sky bake fidelity.**
7. **Instance-aware payloads** — heavy instancing currently re-ships each copy's mesh;
   output is correct, upload size is not optimal.
8. Whatever the feedback ranks higher than the list above.

## Reporting feedback

Please open an issue on the GitHub repository with:
- the plugin version (Help → About) and, if known, the server version,
- a minimal scene (or a description of the material/light/feature involved),
- the native render and the farm render, if you can share them.

Scenes that render *differently* are exactly the input this alpha needs — the corpus
above grew out of reports like yours.
