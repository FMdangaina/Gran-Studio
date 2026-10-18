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
- [x] 4b. GT5 packed vertices (`Mdl3PackedShape`, `gtdev mdl3-packed`): segments, per-shape stream descriptions (0x94 p3),
  fixed-point positions in one 16-bit grid per car (position = grid x F / 2^21 + F + G, exact against plain twins), normals,
  UVs, LODs. All 204,792 GT5 packed shapes decode, every vertex inside its bbox (2 shapes: 1 vertex out).
  Visual check: RX-7 GT (00010075), RX-7, GT-R (00030128), race 00010095 look whole
- [x] 4c. GT5 packed indices: Edge-style values + 2-bit codes (3 new, 0/1 share an edge), size rule exact on all segments.
  Open: shape +26 higher than the sum on ~1% of shapes (dropped degenerates?), 5 last LOD levels, segment +08/+0C blocks
- [x] 4c.1 Export fixes from `ISSUES.md` (checked by the user in ZModeler 2.2.6: meshes import correctly; the export format
  will change when it has to be usable, item 6): shared decoder `Mdl3Mesh`;
  readable object names from the Maya paths (`ShapeNaming`); only the visible models (no shadow / self-shadow / light
  mask copies, `--all-models` for all); one file per LOD level; `gtdev mdl3-gltf` with the node hierarchy.
  `gtdev mdl3-normals`: low LODs reuse the LOD0 normals (agreement 99.5% LOD0 -> 89.7% LOD4 on GT5 race), winding stays
  consistent, so not a decoding error. Open: suspension parts (discs, calipers) have no position in the MDL3
  (op6 = engine matrix slot), segment +08 LOD streams (maybe LOD normals/positions)
- [ ] 4d. GT6 `PMSH` packed meshes (bit layouts, packed/raw vertices, packed indices). **Decoded** for the data held
  in the MDL3 (`Mdl3Pmsh.Decode`, `gtdev pmsh-check`: 4717 interior/wheel/tire/carparts files, 61,397 blocks, 0 positions
  outside the box, 3 blocks with unused vertices); `mdl3-obj` / `mdl3-gltf` export them. Demio interior OK for the
  user (ZModeler); wheel was wrong (double-buffered vertex loads), fixed: to recheck (`tests\pmsh\demio_wheel`).
  Left: the 3 blocks, GT6 bodies (same format, data in `body_s`: with item 5)
- [ ] 4e. Check over every car file, 0 failures

## Next

5. **GT6 `body_s`** — container known (3d: stream map, bundles, images); left: the shape data in the bundles,
   the meaning of the `/` sub streams and `custom_*` groups, then GT6 vs GT5 differences.
6. **Viewer** — Direct3D 11 (Vortice.Windows), read only, in the style of Forza Studio: shows the model **hierarchy**
   (models, Maya groups, shapes, switch variants, LODs) so the user understands how a car is built.
   Exports must work in ZModeler 2.2.6: OBJ (the user does not want 3DS; glTF is not read there).
7. **Materials and textures for display** — every sampler of a material to its texture (`gtdev mdl3-samplers`: diffuse,
   ambient, specular, alpha, normal, dirt, emission...; texture names keep the Polyphony source paths), then the
   external ones (sampler 0x8000 | n, provided by the game):
   - race numbers / plates (`Decken`, `number_f/r`, rally plaque): GT5 `car\decken` holds 402 TXS3 files (`a.00`...);
   - meters (`ExtraMeter_meter_0N`, `custom_meter_*`): drawn at run time from `car\meter` (STRB);
   - paint colour shift (`*_PaintExtraBRDF` reflect/scatter, GT6): the `body_s` `/` sub streams (one per paint colour,
     `ReflectColorShiftBRDF` / `ScatterColorShiftBRDF` images); the paint colour itself comes from the car data (specdb).
   GT5 has almost none (50 references, decals); GT6 thousands.
8. **Car list ("open game folder")** — pick a car by name instead of by id, as in Forza Studio:
   - GT5: `specdb\GT5_JP3009` (GTDB tables, readable with PDTools.SpecDB, MIT): `VARIATION.ModelCode` = the model folder
     name (`00010075`; 1017 of 1088 found as strings in `UnistrDB.sdb`), `GENERIC_CAR` label (`rx7_spiritr_a_02`),
     `CAR_NAME_<language>` (name, grade, short name), maker, year;
   - GT6: `car\MMMM\NNNN` = the same code (`00010112`); 963 of 1227 codes are also in the GT5 database; the others need
     `specdb\GT6\<region>\CarName.dat` (`NDB0`, compressed strings) or `DB0106.dat` (encrypted, not in PDTools);
   - thumbnails: GT5 `car\thumbnail_M` (2110), GT6 `car\thumbnail_S` / `_L` (1293), TXS3 named `<label>_<colour>`;
   - first a `gtdev` car list (code, label, maker, name, files present), then a window in the viewer (item 6).

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
