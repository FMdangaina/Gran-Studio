# Roadmap

One **Now** item at a time; it is closed before the next one starts.
Input data: extracted volumes in `F:\GT_DEV\GT5_VOL` and `F:\GT_DEV\GT6_VOL` (read only).
Tests and dumps: `tests\<topic>` (not committed).

## Now

### 4. Geometry
Decode every shape of every car file to triangles (positions, normals, UVs) and check them visually (OBJ export).
Done when every GT5 and GT6 car/interior/wheel shape decodes (positions inside the shape bbox, indices in range,
triangle count = shape +26) and sample cars look whole. Findings in `docs/mdl3.md`.

- [x] 4a. Plain shapes to OBJ (`gtdev mdl3-obj`): positions, CMP normals, UVs (V flipped), `.mtl` with the diffuse/alpha
  maps as PNG. GT5 wheel `wheel\hq\00010098.04` checked by the user: perfect. GT5 `car\hq\00010075`: everything but the body.
  Open: wheel `Shadow` format has S1 positions (scale about bbox x2, not exact; not visible geometry)
- [ ] 4b. GT5 packed vertices: shape packed ref (+2C, 0x40 bytes) + geometry block 0x94 -> positions/normals/UVs
  (first test: the 3 `GTBE_BODY` shapes of `car\hq\00010075`)
- [ ] 4c. GT5 packed indices (PS3 Edge compressed?)
- [ ] 4d. GT6 `PMSH` packed meshes (bit layouts, packed/raw vertices, packed indices)
- [ ] 4e. Check over every car file, 0 failures

## Next

5. **GT6 `body_s`** — container known (3d: stream map, bundles, images); left: the shape data in the bundles,
   the meaning of the `/` sub streams and `custom_*` groups, then GT6 vs GT5 differences.
6. **Viewer** — Direct3D 11 (Vortice.Windows), read only.

## Later / open questions

- Compare results with Nenkai's PDTools model readers (MIT), note differences in `docs/`.
- Earlier research in `Geom_research\` (car 00010007) is unverified: re-check it, do not build on it.

## Done

- **1. Project setup** (2026-10-17) — repo, solution (`GTFormats`, `GTViewer`, `gtdev`), license, notices.
- **2. MDL3 inventory** (2026-10-17) — header and every table of 24,922 MDL3 explained; findings in `docs/mdl3.md`.

  Read the header and top-level tables of every MDL3 in GT5 (`car\hq`, `car\race`,
  `car\interior`, `wheel`) and GT6 (`car\...\body`, `wheel`, `interior`).

- [x] 2a. `gtdev mdl3-survey`: 24,922 MDL3 found, versions per folder, header ranges (cars: GT5 v8, GT6 v14)
- [x] 2b. Fixed blocks verified (materials 0xE4, SHDS 0xFC, TXS3, PMSH in v14) and key tables (name -> index)
- [x] 2c. models and shapes records; model command streams decode exactly on all 4630 car files, every shape is drawn by a model
  (open: meaning of the switch values, shape flags; vertex/packed data is item 4)
- [x] 2d. fvf (vertex format) records: layout exact on all files, positions inside the bbox on all 57,043 GT5 plain shapes,
  CMP normals verified, morph arrays (hi/low blend, moving wings) understood. Open: UV scale -> 2e
- [x] 2e. materials / shaders / texture set headers: layout verified on all 4630 car files (`gtdev mdl3-matcheck`, 0 failures),
  render state and texture info are RSX register streams, sampler bit 15 = external texture, shader chain to SHDS vertex programs.
  UV scale: none for GT5 plain shapes (S1/32767, UB/255, F as is; F > 1 = tiled textures with wrap repeat).
  Found for item 3: GT6 image offsets are 0 (pixels not in `body`); GT5 has data after the last image.
  Open: material header byte +04, material data +18/+1C/+24, SHDS second program table -> 2f
- [x] 2f. remaining tables, byte coverage (`gtdev mdl3-coverage`) on 16,808 car/interior/wheel files: GT6 v14 every byte
  explained, 0 failures; GT5 v6/v8 everything but the packed geometry in the shapes region. Decoded: 0x54 named float blocks
  (SH light masks, occlusion quads), VM host registers/outputs/bytecode/context, 0x78 per-material runtime records,
  0x88 shader references + passes, SHDS fragment programs and +38/+3C/+40 (FP microcode = GT5 "data after the last image"),
  0xB0 model material lists, 0xB8 material parameter animation, joints (the "wing data"), rigid bodies (0xA4),
  GT5 soft bodies (0xC0), GT5 geometry block 0x94, PMSH meshes and smooth data sizes, stream map 0xDC to `body_s`.
  Open (data located, meaning unknown): material block +04, 0x78 n, 0xB0 +28..+2E, a few u16 fields; 17 old v1/v2/v4 files not covered.
  For item 4: GT5 packed geometry (0x94 + shape packed refs, looks like PS3 Edge), GT6 PMSH data (exact sizes known).
  For item 5: stream map 0xDC lists the `body_s` deflate chunks.
- **3. TXS3 textures** (2026-10-17) — every in-file image decodes, GT6 body pixels found in `body_s`; findings in `docs/txs3.md`.
  Reference: Nenkai's TXS3Converter (MIT, cloned in `ref\TXS3Converter`, uses PDTools' TextureSet3 and PS3 Swizzler).

- [x] 3a. Formats (`gtdev txs3-survey`): only DXT1/DXT23/DXT45/A8R8G8B8 (swizzled or linear); image record fully mapped
  (+08 cube, +0B location, +10 depth for 3D textures); remap 0xAA1B = A8R8G8B8 bytes B,G,R,A
- [x] 3b. Size rule exact on all 23,224 car/interior/wheel/tire/carparts files: faces padded to 128, linear pitch on every
  level, non-pow2 mipmapped linear allocated at pow2 pitch
- [x] 3c. Decode (`Txs3Image`, `gtdev txs3-png`): every in-file image decodes, 0 failures; visual check OK on decals, calipers,
  wheels, linear, cube. Open: 3D DXT block order (looks right, confirm in the viewer), +0B on 7 BRDF images
- [x] 3d. GT6 body pixels are in `body_s` (`Mdl3StreamMap`, `gtdev body-s`): the 0xDC stream map tiles `body_s` exactly,
  each stream = deflate/raw chunks -> bundle (header with shape + image tables, then data); all 168,414 offset-0 images
  found and decoded, 0 failures; extra copies in variant bundles are identical; `txs3-png` reads them. RX-8 checked visually.
  Open for item 5: `/` sub streams = paint colours? (BRDF images), shape data in the bundles
