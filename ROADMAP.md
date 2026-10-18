# Roadmap

One **Now** item at a time; it is closed before the next one starts.
Input data: extracted volumes in `F:\GT_DEV\GT5_VOL` and `F:\GT_DEV\GT6_VOL` (read only).
Tests and dumps: `tests\<topic>` (not committed).

## Now

### 6. Viewer
Direct3D 11 (Vortice.Windows), read only, in the style of Forza Studio: a **TreeView** with the model hierarchy
(models, Maya groups, shapes, switch variants, custom parts, LODs) next to the 3D view, so the user understands how a
car is built (requested by the user 2026-10-18). Done when the user opens a GT5 and a GT6 car (body, interior, wheel)
and finds the tree and the 3D view right.

- [x] 6a. Scene tree in `GTFormats` (`Mdl3Scene`, shared by viewer and gtdev): models -> Maya path nodes -> shapes,
  every op5/op54 switch as a selector with named branches (custom parts = `body_s` group, or the `CUSTOM_x` Maya node in
  interiors; state switches = the Maya words only their branch has: DAYTIME / NIGHT, BRAKE_ON...), the branches each
  shape is drawn in, LOD level from `LEVEL_n`; `ShapeNaming` moved to GTFormats. `gtdev mdl3-scene <file>` prints it;
  survey over 17,012 car/wheel files: only the 19 known MDL3 v1-4 files fail
- [x] 6b. Renderer (Vortice 3.8.3, D3D11, WARP fallback): GPU meshes from `Mdl3Mesh`, two-sided Lambert, wireframe,
  orbit camera; `GTViewer --shot <file> <out.png> [--yaw --pitch --lod --set sel=branch --wire --flat --obj]`
  renders off screen (checked: Demio GT6 body/interior/wheel, RX-7 GT5 hq night, race 00010095 LOD0-3, Roadster wing
  option 3, GT5 wheel)
- [ ] 6c. Window: File > Open (or drop a file / give it on the command line), TreeView with check boxes (show / hide;
  shapes grey when the switches or LOD do not draw them; switched shapes show their options), switches and LOD
  pickers, selection highlighted in orange + status bar (shape, material, triangles, Maya path), double-click or S
  frames the selection, F frames all, Ctrl+W wireframe, Ctrl+T textures. At load: helper models hidden (shadow,
  self-shadow, light mask, menu cabin, occlusion, FIXn instance copies). **To check by the user**
- [x] 6d. Diffuse textures (in the file or GT6 `body_s`); parts without a diffuse map (paint, GT6 wheels) get a soft
  colour per material: real colours are item 7
- [ ] 6f. Car list by name (requested by the user during 6, takes the first part of item 8): File > Open game folder
  (Ctrl+G) on GT5_VOL or GT6_VOL -> list with search, year, files, thumbnail; buttons open hq/race body, interior,
  wheels; the car name goes in the title and the tree root. `GtDb` (GTDB reader, re-implemented from PDTools.SpecDB,
  MIT) + `CarCatalog`; `gtdev car-list <root> <out.tsv>`, `gtdev specdb-dump`. GT5: 1088/1088 named from the specdb
  (VARIATION.ModelCode -> CAR_NAME_british, GENERIC_CAR year), all with thumbnail. GT6: 1024 from the GT5 specdb
  next to it, 169 guessed from the Maya project paths in the model (shown as guessed), 34 without name; 1063
  thumbnails (stand-alone TXS3, `Txs3Image.DecodeFile`). **To check by the user**
- [ ] 6e. File > Export visible to OBJ (+ MTL + PNG): what is shown, current switches and LOD (Demio body: 99 objects,
  152,758 faces). **To check by the user in ZModeler**

Exports must work in ZModeler 2.2.6: OBJ only (the user does not want 3DS; glTF is not read there). ZModeler 2 ignores
OBJ `o`/`g` and names the objects after the `usemtl` material (`m000`...): readable names there would need one
material name per shape (later).

## Next

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
- **4. Geometry** (2026-10-18, **closed with reservations** by the user, for very small parts) — every shape of every
  GT5/GT6 car, interior, wheel, tire and carparts file decodes to triangles (positions, normals, UVs); OBJ/glTF export
  checked by the user in ZModeler 2.2.6; findings in `docs/mdl3.md`. Reservations, left open:
  - 3 GT5 race shapes: 00800005 shapes 75 and 119 have 1 vertex 1 mm outside the box (known), 00740016 shape 72 has
    1 wrong index at the last LOD level;
  - 19 files of an older MDL3 version (1-4: 14 GT5 interiors, 3 wheels, 2 tires, e.g. tire 0661) have another header
    and are not read;
  - GT6: 14 body + 3 interior blocks with unused vertices, 11 flag 0x01 race blocks (position only, outside the box).

  Decode every shape of every car file to triangles (positions, normals, UVs) and check them visually (OBJ export).
  Done when every GT5 and GT6 car/interior/wheel shape decodes (positions inside the shape bbox, indices in range,
  triangle count = shape +26) and sample cars look whole.

- [x] 4a. Plain shapes to OBJ (`gtdev mdl3-obj`): positions, CMP normals, UVs (V flipped), `.mtl` with the diffuse/alpha
  maps as PNG. GT5 wheel `wheel\hq\00010098.04` checked by the user: perfect.
  Open: wheel `Shadow` format has S1 positions (see 4e)
- [x] 4b. GT5 packed vertices (`Mdl3PackedShape`, `gtdev mdl3-packed`): segments, per-shape stream descriptions (0x94 p3),
  fixed-point positions in one 16-bit grid per car (position = grid x F / 2^21 + F + G, exact against plain twins), normals,
  UVs, LODs. All 204,792 GT5 packed shapes decode. Visual check: RX-7 GT (00010075), RX-7, GT-R (00030128), race 00010095
- [x] 4c. GT5 packed indices: Edge-style values + 2-bit codes (3 new, 0/1 share an edge), size rule exact on all segments.
  Open: shape +26 higher than the sum on ~1% of shapes (dropped degenerates?), segment +08/+0C blocks
- [x] 4c.1 Export fixes from `ISSUES.md` (checked by the user in ZModeler 2.2.6): shared decoder `Mdl3Mesh`; readable
  object names from the Maya paths (`ShapeNaming`); only the visible models (`--all-models` for all); one file per LOD
  level; `gtdev mdl3-gltf` with the node hierarchy. `gtdev mdl3-normals`: low LODs reuse the LOD0 normals (not a
  decoding error). Open: suspension parts (discs, calipers) have no position in the MDL3 (op6 = engine matrix slot)
- [x] 4d. GT6 `PMSH` packed meshes: in-file data (`Mdl3Pmsh.Decode`, `gtdev pmsh-check`: 4717 files, 61,397 blocks) and
  bodies from the `body_s` bundles (`Mdl3BodyS`: 2454 bodies, 267,778 blocks, 0 exceptions, 99.47% triangles agree with
  the normals). Demio interior, wheel and body checked by the user in ZModeler
- [x] 4e. Check over every car file (`gtdev mesh-check`, `tests\mesh_check`): about 900,000 shapes, **all decode** with the
  shared decoder. Found on the way: GT5 plain triangle strips (shape flags nibble 6, restart +20), S1 shadow positions =
  raw / 2^k (k from the shape bbox; where the game stores it is open), empty tire templates
- **5. GT6 `body_s`** (closed 2026-10-18) — colour variations, custom parts (op54), GT5 vs GT6; findings in `docs/mdl3.md`.
  Container known (3d: stream map, bundles, images), shape data decoded (4d). Explain what the parts of the stream map
  are for. Done when every `/` sub stream and every `custom_*` item of the 2454 GT6 bodies has a known meaning (checked
  against the game data) and the GT6 vs GT5 differences of the same car are written down.

- [x] 5a. `/` sub streams (`gtdev bodys-subs`, all 2454 bodies): **colour variations**. Materials `..._CLR_VARI_k`
  (GT6 only); `/` has 2 x (V + 1) sub streams, two halves, slot k = images of the `CLR_VARI_k` materials (12,802 of
  13,461 non-empty slots; the rest = shared copies in half A and the last slots), last slot = `_PaintExtraBRDF`
  materials (special paints). Half B = body paint, half A = soft top / roof / stripes / cabin colours (empty on 1891
  bodies). No shapes, only images (mostly colour-shift BRDF).
  Open: which paint colour picks which variation (GT6 database, encrypted SQLite), why two halves
- [x] 5b. `custom_*` groups (`gtdev bodys-custom`): optional parts, a bonnet, b muffler, c rear wing, d front bumper /
  spoiler, e flat floor, f roof cover, brake discs, calipers; item k = option k. Drawn by the v14 switch **op54**
  (VM register = base + rank of the part among the car's custom parts a, b, brake, c, caliper, d, e, f; 2454 / 2454;
  base = end of the VM outputs, 786 / 786): branch k of all op54 of a register = exactly the shapes of item k
  (18,119 / 18,119; some op54 start every branch with an op15 pop). Custom items with sub streams = colour
  variations (70). Open: the base on bodies without VM outputs (after the VM locals, from the bytecode)
- [x] 5c. GT5 vs GT6 (`gtdev gt5-gt6`): 1024 cars in both, 203 GT6 only. Same model list + `GTBE_MENU_INTERIOR` and
  `GTBE_OCCLUSION` in every GT6 car; same Maya scene reworked (median 63% of the GT5 shape paths kept); GT6 adds
  CLR_VARI materials, moves geometry to PMSH in `body_s` and custom parts to the `body_s` groups; fewer bones
