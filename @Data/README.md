# @Data — local-only test fixtures

Everything under `@Data/` except this README is **git-ignored**: the smoke
scenes are Autodesk 3ds Max sample content (size and license both forbid
committing them). The 3dsmaxbatch smoke tests in
`OutWit.Render.3dsMax.Plugin.Export.Tests` resolve scenes against this folder
and `Assert.Ignore` cleanly when a scene is missing — so a checkout without
`@Data` still builds and passes the unit suite.

## Layout the smoke tests expect

```
@Data/3ds_max/
├── Scenes/
│   ├── Raytrace/AdvancedExamples/     # A01depth.max, A03Metal.max, A07cglas.max (+ local textures, e.g. CEDFENCE.JPG)
│   ├── Characters/Complete/           # TarrasqueTextured.max
│   ├── Mixamo/                        # house_dancing_413.max
│   ├── ViewportRendering/             # robby_vs_fly.max
│   └── Crowd/MotionClips/             # Eagles.max
└── Maps and Materials/                # shared sample maps tree (bitmap lookups)
```

## Where to get the content

The scenes are the standard **Autodesk 3ds Max sample files** (the
`Scenes`, `Maps and Materials` trees from the 3ds Max samples distribution).
If you have the WitEngine parent workspace checked out, copy the folders
listed above from `WitEngine/@Data/3ds_max/`; otherwise install the samples
that ship with 3ds Max and mirror the layout.

Smoke prerequisites beyond `@Data`:

- a local **3ds Max 2027** install (`3dsmaxbatch.exe` is auto-discovered via
  `ADSK_3DSMAX_x64_*` environment variables or `C:\Program Files\Autodesk\`),
- `OMNIBUSCLOUD_API_KEY` (see `.env`) for the upload / connected-render smokes.
