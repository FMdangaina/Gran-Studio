# Roadmap

One **Now** item at a time; it is closed before the next one starts.
Input data: extracted volumes in `F:\GT_DEV\GT5_VOL` and `F:\GT_DEV\GT6_VOL` (read only).
Tests and dumps: `tests\<topic>` (not committed).

## Now

(empty)

## Next

(empty)

## Closed by the user on 2026-10-19 (12, 11.1, 11.2)

12. **Readable object names in ZModeler** (user: "mi piacerebbe assai"; closed: "ci siamo") â€” ZModeler 2.2.6 ignores OBJ `o`/`g` and
   names the objects after `usemtl`: write one material per object, named like the object (`black.s14`,
   `BODY.bonnet.s169`), same texture and colour, so the objects are readable in ZModeler. Viewer export and `gtdev`.
   - [x] a. one material per object (`GTFormats/Mdl3/ObjMaterials.cs`, shared by the viewer and `gtdev mdl3-obj`):
     `newmtl` = the object name (letters, digits, `._-`, max 60, repeats `_2`), Kd from `diffuseColor0` when the
     material has it, `# GT material N name (shader)` comment. Before: `m000`, `m001`... (user screenshot 2026-10-19).
     2026-10-19 second round (user: "non voglio solo i mesh ma anche i nomi materiali"): the name is now
     **`object__GTmaterial`** (`BODY.LEVEL_0.BLIND.s5__BLIND1`, `BODY.RetractableLight_LEFT.s1__BODY_DIRT`; material
     part without namespace and `_CLR_VARI_k`, max 32), so ZModeler shows both. Checked on 10 models (5 cars: body,
     cockpit, wheel). Names can reach ~95 characters: **to check that ZModeler takes them**.
   - [x] b. all LOD levels, **one folder per level** (user 2026-10-19: "falcon / lod0 / its meshes and textures"):
     File > Export visible, all LOD levels (Ctrl+Shift+E) -> `<name>\lod0\<name>.obj + .mtl + textures`, `lod1\`...;
     default name = the car name from the car list. `gtdev mdl3-obj` does the same when there are several levels;
     `GTViewer --shot <file> <png> --obj-lods <dir\name>` = off-screen check (GT6 0001/0012 race: lod0 81 objects
     53 textures, lod1 79 / 53, lod2 21 / 16). **To check by the user**
     2026-10-19 second round (user: "lod0..nMax, cockpit, wheel lod0..nmax"): File > **Export the car to OBJ**
     (Ctrl+Shift+E) writes `<name>\lod0..N` (the open body as shown), `<name>\cockpit` (the interior file; `cockpit\lodN`
     when it has levels) and `<name>\wheel\lod0..N` (the wheel of the same quality: GT6 `hq|race\wheel`, GT5
     `wheel\hq\CODE`), every folder with its obj/mtl/textures (`ObjExport.WriteCar`, the car's files from the path:
     `CarCatalog.FromModelPath`). GT5 `car\interior` files hold no shapes (the GT5 cockpit is in the hq body): no
     cockpit folder then. Off screen: `GTViewer --shot <body> <png> --obj-car <dir\name>`; 5 cars exported
     (`tests\tex_full\export`). A model that is not a body (wheel, part): its levels only.
     Third round (user: "export completo per id macchina da hq race interior wheels, sia GT5 che GT6"): the export
     takes the whole car code from any of its files: `<name>\hq\lodN`, `\race\lodN`, `\cockpit`, `\wheel\hq|race\lodN`
     (GT6) or `\wheel\CODE\lodN` (GT5; the `CODE.NN` wheels are aftermarket rims, user: "non mi serve esportarli":
     skipped); the open file as shown, the others as loaded. Checked: GT6 MX-5 (from its interior), R18; GT5 RX-7,
     HPA TT (from an aftermarket wheel file: only wheel 01890002) in `tests\tex_full\export_id`.
   - [x] c. **all the textures**: every in-file texture a material uses is saved next to the OBJ, named after its
     Polyphony texture name (`front_light_OFF_Color_DXT5.png`): diffuse / Raytrace colour = `map_Kd`, alpha = `map_d`,
     specular = `map_Ks`, normal = `bump`, others (dirt, occlusion, BRDF, env...) as `#` comments in the .mtl.
     2026-10-19 second round (user: "voglio vedere il modello completamente texturizzato e l'esportazione che mi porta
     per ogni mesh il suo file texture di riferimento"), analysis on 10 models (`gtdev tex-full`, `tests\tex_full\tex_full.txt`):
     - why GT6 parts had no texture: half the GT6 shapes use materials **without any diffuse map and without colour
       constants**; their colour is baked as an inline constant in the material's fragment program. Read now
       (`Mdl3ShaderColor`, first MOV/MUL/MAD .xyz constant in 0..1, not black/white/magenta): rubber 0.01-0.05, black
       plastics 0.05, metal 0.27, mirror 0.30 0.32 0.31, glass 0.27 0.30 0.27, orange winker covers 0.79 0.36 0, red
       covers 0.7 0 0. Wrong on some (`_BODY_BLACK` gives 0.83 grey): a heuristic, not a shader emulation.
     - **paint**: the `_CLR_VARI_` materials take the colour-shift image of the body colour (`ScatterColorShiftBRDF`
       4x1 / 32x1 = the base colour: MX-5 red 129 1 6, RX-8 silver 220 221 221).
     - vertex colours (`colorSet1`, now decoded in `Mdl3Mesh.Colors`) are grey occlusion, not colours (not drawn yet).
     - every material has a **look** (`Mdl3TextureSet.Look`: texture + colour + source); the viewer draws it (Textures
       on; off = the old soft colours to tell parts apart) and the export writes Kd and **map_Kd for every object**:
       its texture, else an 8x8 `colour_RRGGBB.png` of its colour. MX-5: 29 textured shapes, 12 paint, 29 shader
       colour, 9 grey (before: 25 textured, the rest pastel).
     - the **external** samplers are not files: the game draws them at run time (race numbers, number plates, extra
       meters, paint BRDF). On disk only: `car\decken` (GT6 800 TXS3 = fonts a-h x numbers 00-99, 64x64; GT5 the same)
       and the digits in each car's `meter` file (STRB with TXS3 blocks, LCD segments). Decken materials now show race
       number `a.01` (placeholder); the meter digits are listed (11.2). Number plates / extra meters stay a colour.
     Checked by the user.

## Closed by the user on 2026-10-19 (7, 8, 9)

7. **Materials and textures for display** (closed: "the materials show"; the search for every texture in the game
   files goes on as 12c) â€” every sampler of a material to its texture (`gtdev mdl3-samplers`: diffuse,
   ambient, specular, alpha, normal, dirt, emission...; texture names keep the Polyphony source paths), then the
   external ones (sampler 0x8000 | n, provided by the game):
   - race numbers / plates (`Decken`, `number_f/r`, rally plaque): GT5 `car\decken` holds 402 TXS3 files (`a.00`...);
   - meters (`ExtraMeter_meter_0N`, `custom_meter_*`): drawn at run time from `car\meter` (STRB);
   - paint colour shift (`*_PaintExtraBRDF` reflect/scatter, GT6): the `body_s` `/` sub streams (one per paint colour,
     `ReflectColorShiftBRDF` / `ScatterColorShiftBRDF` images); the paint colour itself comes from the car data (specdb).
   GT5 has almost none (50 references, decals); GT6 thousands.
8. **Car list ("open game folder")** (closed: "we have a car list that shows easily what we open") â€” pick a car by name instead of by id, as in Forza Studio:
   - GT5: `specdb\GT5_JP3009` (GTDB tables, readable with PDTools.SpecDB, MIT): `VARIATION.ModelCode` = the model folder
     name (`00010075`; 1017 of 1088 found as strings in `UnistrDB.sdb`), `GENERIC_CAR` label (`rx7_spiritr_a_02`),
     `CAR_NAME_<language>` (name, grade, short name), maker, year;
   - GT6: `car\MMMM\NNNN` = the same code (`00010112`); 963 of 1227 codes are also in the GT5 database; the others need
     `specdb\GT6\<region>\CarName.dat` (`NDB0`, compressed strings) or `DB0106.dat` (encrypted, not in PDTools);
   - thumbnails: GT5 `car\thumbnail_M` (2110), GT6 `car\thumbnail_S` / `_L` (1293), TXS3 named `<label>_<colour>`;
   - first a `gtdev` car list (code, label, maker, name, files present), then a window in the viewer (item 6).
   **Mostly done in 6f** (car list window, GT5 all named, GT6 1193 of 1227). 2026-10-18: GT6 now reads its own
   database: `DB0106.dat` = SQLite encrypted with Salsa20 (key published by Nenkai, GTToolsSharp / modding guides),
   `Gt6SpecDb` decrypts it to `%LOCALAPPDATA%\GTViewer\specdb` and joins GENERIC_CAR.ModelCode (maker * 10000 + car =
   the model folders) with CAR_NAME_ALPHABET: **1227 of 1227 GT6 cars named, all with thumbnail**. Waiting for the
   user to close it.
   2026-10-18 (user's request with a screenshot): the list is now **docked under the 3D view** (`CarListPanel`, View >
   Car list Ctrl+L, the last game folder comes back at start) with tabs **Cars / Wheels / Wing parts** (wheels apart
   from the cars: each car's wheels + the 82 GT6 aftermarket wheels by name, `carparts`), and the selected car shows a
   **sheet from the GT6 database** (maker, country, price, PP, engine, power, torque, red line, drive, weight, size,
   tyres, colours; GT5 cars use the GT6 row, 1064 / 1088). Fixed: GT6 names were shown as "(guessed)". UI checked
   off screen with `GTViewer --ui-shot <root> <tab> <search> <out.png>`. **To check by the user.**
9. **Car data from the spec databases** (closed for now: "the data we read is enough as a base"; b and c left, not
   planned) â€” the real specs of every car, GT5 (GTDB) and GT6 (SQLite, 65
   tables, `gtdev gt6-specdb`): engine (power, torque curve torqueA-X over rpmA-X, rev limit, sound), chassis (mass,
   size, weight split, cd/cl, ride height), gears and final drive, drivetrain, suspension (springs, dampers, camber,
   toe, anti-roll), brakes, tyres, colours (VARIATION: names per paint = the `body_s` colour slots of item 5a), custom
   parts (CAR_CUSTOM_INFO bits = the custom_* groups of item 5b). Steps:
   - a. a car sheet in the viewer (and `gtdev`) from DEFAULT_PARTS -> part tables, with units checked (first part done
     with item 8: dealer sheet + engine curve + chassis size + tyres, units in docs/specdb.md; left: gears, suspension,
     brakes, aero, tyres compounds);
   - b. same sheet for GT5 (GTDB column layouts from PDTools' table mappings);
   - c. export as a neutral file (JSON / CSV) that Forza Studio can read: mapping GT fields -> FM4 gamedb
     (Data_Car, Data_Engine, torque curve, gears...), unit conversions, what has no equivalent. The import itself
     belongs to the Forza Studio repo, as its own topic.

## Later / open questions

- Compare results with Nenkai's PDTools model readers (MIT), note differences in `docs/`.
- Earlier research in `Geom_research\` (car 00010007) is unverified: re-check it, do not build on it.

## Done

- **1. Project setup** (2026-10-17) â€” repo, solution (`GTFormats`, `GTViewer`, `gtdev`), license, notices.
- **2. MDL3 inventory** (2026-10-17) â€” header and every table of 24,922 MDL3 explained; findings in `docs/mdl3.md`.

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
- **3. TXS3 textures** (2026-10-17) â€” every in-file image decodes, GT6 body pixels found in `body_s`; findings in `docs/txs3.md`.
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
- **4. Geometry** (2026-10-18, **closed with reservations** by the user, for very small parts) â€” every shape of every
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
- **5. GT6 `body_s`** (closed 2026-10-18) â€” colour variations, custom parts (op54), GT5 vs GT6; findings in `docs/mdl3.md`.
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
- **6. Viewer** (2026-10-18, **closed with reservations** by the user: works, details not checked yet; open: 6e OBJ export not checked in ZModeler, UVShiftControl materials left as stored, paint colours = item 7)
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
- [x] 6c. Window: File > Open (or drop a file / give it on the command line), TreeView with check boxes (show / hide;
  shapes grey when the switches or LOD do not draw them; switched shapes show their options), switches and LOD
  pickers, selection highlighted in orange + status bar (shape, material, triangles, Maya path), double-click or S
  frames the selection, F frames all, Ctrl+W wireframe, Ctrl+T textures. At load: helper models hidden (shadow,
  self-shadow, light mask, menu cabin, occlusion, FIXn instance copies). Used by the user (Castrol Supra, GT6)
- [x] 6d. Diffuse textures (in the file or GT6 `body_s`); parts without a diffuse map (paint, GT6 wheels) get a soft
  colour per material: real colours are item 7. Textures blend over a neutral paint by their alpha (decal sheets).
  Livery "messed up" on the Castrol Supra (user, 2026-10-18): the UVs are transformed by each material's vertex
  program (MOV / MUL x1, x2, x4, x8 / MAD with the runtime `UVShiftControl`): `Mdl3UvScale`, applied in
  `Mdl3Mesh.Decode` (viewer and exports); see docs/mdl3.md "UV transform"
- [ ] 6f. Car list by name (requested by the user during 6, takes the first part of item 8): File > Open game folder
  (Ctrl+G) on GT5_VOL or GT6_VOL -> list with search, year, files, thumbnail; buttons open hq/race body, interior,
  wheels; the car name goes in the title and the tree root. `GtDb` (GTDB reader, re-implemented from PDTools.SpecDB,
  MIT) + `CarCatalog`; `gtdev car-list <root> <out.tsv>`, `gtdev specdb-dump`. GT5: 1088/1088 named from the specdb
  (VARIATION.ModelCode -> CAR_NAME_british, GENERIC_CAR year), all with thumbnail. GT6: 1024 from the GT5 specdb
  next to it, 169 guessed from the Maya project paths in the model (shown as guessed), 34 without name; 1063
  thumbnails (stand-alone TXS3, `Txs3Image.DecodeFile`). Used by the user
- [ ] 6e. File > Export visible to OBJ (+ MTL + PNG): what is shown, current switches and LOD (Demio body: 99 objects,
  152,758 faces). **To check by the user in ZModeler**

Exports must work in ZModeler 2.2.6: OBJ only (the user does not want 3DS; glTF is not read there). ZModeler 2 ignores
OBJ `o`/`g` and names the objects after the `usemtl` material (`m000`...): readable names there would need one
material name per shape (later).

- **10. LOD levels** (closed by the user 2026-10-18; request from the AEM S2000 GT6 race body) â€” "confusion with the LODs, I need to
  extract them too": the viewer drew the high and low shapes of GT6 premium bodies on top of each other, the LOD
  picker listed 14 levels from Maya `LEVEL_n` nodes (numbered per part), half the shapes were named `shapeNN`.
  - a. model command **op8 = LOD select** (PS2 `LODSelect` in PDTools): one distance per level and one u16 jump per
    level (as op5), the op9 after it = nothing drawn. All 45,592 GT6 op8 jumps land on a command (`mdl3-models`).
    Done: `Mdl3Command.LodDistances`, `Mdl3Draw.Lods`, `Mdl3Scene.Levels` = op8 levels; LOD k = branch k of every op8
    (clamped to its last); nested op8 (whole-car `LOD_STD_BODY` around part LODs) count from the outer branch: 13
    shapes in 2454 bodies reach no level. Maya `LEVEL_n` agrees with the op8 branch on 47,000 of 51,500 shapes; the
    others are Maya order, not game order (0001/0012: `LEVEL_2` = branch 1).
  - b. viewer: LOD picker from op8, triangles of the shown level in the status bar, tree labels `LOD0` / `LOD1-2`,
    **File > Export visible, all LOD levels to OBJ** (Ctrl+Shift+E: `name_lodN.obj`); `gtdev mdl3-obj` / `mdl3-gltf`
    split GT6 levels too. S2000 race: LOD0 130,279 triangles, LOD1 47,516 (before: both drawn, ~178k).
  - c. shapes without a Maya path (GT6 premium bodies): named and grouped by material (`black.s0`, `carbon_dart1.s14`),
    `(no Maya name)` group; Maya default materials (`pdiCgFlex45`) keep `shape.sN`.
  Count per file (`mdl3-scene` survey): GT5 race 199 files with 5 levels + 33 with 6 (likely premium), 853 with 1;
  GT6 race body 515 with 2, 48 with 3, 660 with 1 (same 787B: GT5 5 levels, GT6 2). GT5 levels = same vertices, fewer
  triangles; GT6 levels = separate meshes. GT5 race levels as low LODs of GT6 cars: **rejected by the user** (2026-10-19).
- **11. Texture viewer** (closed by the user 2026-10-19; "first a viewer of textures only", before item 7) â€” a window that lists
  every TXS3 image of the open file (GT6 bodies: pixels from `body_s`) or of a stand-alone TXS3 file (thumbnails,
  `decken`): thumbnail, texture names, size, format, mips, cube / 3D, the materials and samplers that use it, and the
  external samplers (provided by the game). Big view with zoom, RGB / alpha / RGBA, faces and slices of cubes and 3D
  textures; save the selected one or all as PNG. Done when the user browses the textures of a GT5 and a GT6 car.
  2026-10-19 coded: `Mdl3TextureSet` (GTFormats: images, names, users, external samplers, decode from the file or
  `body_s`, also stand-alone TXS3; the 3D view uses it too), `TextureForm` (View > Texture browser, Ctrl+Shift+T;
  opening or dropping a TXS3 file opens it): thumbnails grouped by sampler (diffuse first), filter, zoom / pan,
  channels, faces / slices, **Upright** (GT rows are stored bottom up; on by default, also for the saved files), info
  panel, save one / all as **PNG or TGA** (32-bit with alpha, user request; the TGA equals the PNG pixel for pixel). `GTViewer --tex-shot <file> <out.png> [image] [channel]` = off-screen check: S2000 GT6
  race body (58 images from `body_s`), GT6 wheel, GT5 hq, GT6 thumbnail, GT5 `decken` (`.00` = empty red, alpha 0).
  Checked by the user.
  - [x] 11.1 (user 2026-10-19: "I want it to be a tab, as few pop-ups as possible") the texture browser is now a
    **Textures tab** next to the **3D view** tab (`TexturePanel`, filled with the open model's textures when the tab is
    shown; Ctrl+Shift+T switches tab; a TXS3 file opens there); Open / Save on the tab's tool bar; open, export and
    car list errors go to the status bar instead of message boxes. `--tex-shot` now shoots the main window.
    Car list (user screenshot, HPA TT with 15 wheels): the wall of "Open ..." buttons is now a **file list** under the
    thumbnail (what / path under the game folder); selecting a row opens the file.
    **To check by the user**
  - [x] 11.2 (user 2026-10-19: "le texture che trovi in giro per il folder vengono indicate, magari con nome in colore
    diverso") the textures of other game files the model uses are in the Textures tab in their own group, **Shared:
    other game files**, names in **blue**, the info panel says "SHARED: from <file>" and the materials that take it:
    the race number (`car\decken\a.01`, for Decken materials) and the car's `meter` digits. Status bar: "N images +
    M shared". Checked off screen (`--tex-shot`, GT6 MX-5). **To check by the user**
