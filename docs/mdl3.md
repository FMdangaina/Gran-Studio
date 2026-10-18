# MDL3 (ModelSet3) — verified findings

Sources: `gtdev mdl3-survey` over every MDL3 in the extracted GT5 and GT6 volumes (24,922 files).
Starting names from Nenkai's PDTools (MIT); a field is listed as *verified* only when the survey confirms it on all files.

## Where MDL3 files are and which versions

| Group | Files | Header version |
|---|---:|---|
| GT5 `car\hq`, `car\race` | 1088 + 1088 | 8 |
| GT5 `car\interior` | 1083 | mostly 6 (824) and 8 (245); a few 1/2/4 |
| GT5 `wheel` | 7414 | 8 (7394) |
| GT5 `tire`, `character`, `crs` | 5380, 540, 190 | mostly 8 |
| GT6 `car\<maker>\<id>\{hq,race}\{body,wheel}`, `interior` | 1227 each | 14 |
| GT6 `carparts`, `tire`, `character`, `crs` | 204, 832, 751, 217 | mostly 14 |

Versions seen overall: 1, 2, 3, 4, 6, 8, 9, 12, 13, 14. The car work targets **v8 (GT5)** and **v14 (GT6)**.
GT6 `body_s` is not MDL3 (it starts with raw deflate data), see roadmap item 5.

## Header (big-endian, 0xE4 bytes)

Verified on all v8 and v14 files:

| Offset | Field | Notes |
|---|---|---|
| 0x00 | magic `MDL3` | |
| 0x04 | file size | equals the real length in every file except one (GT5 `car\race\00040079`: 0x40 short) |
| 0x08 | reloc pointer | always 0 on disk |
| 0x0C | u16 version | see table above |
| 0x0E | u16 runtime flags | always 0 on disk |
| 0x44 | materials offset | always 0xE4 (right after the header) |
| 0x48 | texture set offset | always points to a `TXS3` block |
| 0x4C | shaders offset | always 0xFC, points to `SHDS` |
| 0xD0 | packed mesh header (v14) | always points to `PMSH` in v14; 0 in v8 |
| 0x6C | "vmInstanceOffset" (PDTools) | always the highest value; in 8 v8 files it is past the end of the file: it is probably a size, not an offset |

Always 0 on disk in v8 and v14: 0x1C, 0x24, 0x70, 0x74, 0x80, 0x84, 0xA8, 0xAC, 0xD4, 0xD8.
v8 only: 0xCA..0xE0 are all 0 (packed meshes and the car body stream exist only in later versions).
v14 only: 0xC0/0xC4/0xC6 are all 0 and 0xC8 is always 0xFFFF.
0xB4 == `modelsOffset` (0x30) in every v8 and v14 car file (it goes with the 0xB0 table, one record per model).

Layout order (most common, v8): materials < shaders < 0x78 < models < modelKeys < shapes < shapeKeys < fvf < textureSet < 0x54 < VM bytecode < 0x88 < textureKeys < 0xB0 < 0xB8 < 0xBC, then 0x6C last.
v14 is the same with `PMSH` (0xD0) before the models and the packed mesh keys (0xCC) at the end.

## Key tables (name -> index), verified

Record: `u32 nameOffset, u32 index`, 8 bytes, sorted by name. Names are null-terminated ASCII in a string pool near the end of the file.
The record size of 8 is exact (gap == count * 8) on every file for: shapeKeys, bones*, registerVals*, 0x5C*, wingKeys, and textureKeys in most files.
(*) same 8-byte stride; registerVals (0x58) and 0x5C are the VM host registers and outputs (see 2f below), bones are 0x48.

- **modelKeys** (0x12 count, 0x34 offset): model names, e.g. `GTBE_BODY`, `GTBE_INTERIOR_SHARE`, `GTBE_SHADOW_BOX`, `GTBE_SS_BODY`.
- **shapeKeys** (0x16, 0x3C): the Maya node path of each shape, e.g.
  `|GTBE_BODY|FIX|transform1|L1_BMFPP_NOEXPAND|Body_BMFPP_NOEXPAND|FDBody_Base|FDBody_BaseShapeShape`.
  The same name can map to several shape indices (one mesh split by material).
- **textureKeys** (0x8E, 0x90): sampler names (`diffuseMapSampler`, `reflectColorShiftMapSampler`, ...).
- **bones** record is 0x48 bytes, not 8 (exact on all 2777 v8 / 2294 v14 files that have bones).

## Models (header 0x10 count, 0x30 offset) — verified

0x30-byte records (`Mdl3Model.cs`). Checked on all car files (GT5 hq/race v8: 2176 files, 19,584 models;
GT6 hq/race body v14: 2454 files, 26,994 models) with `gtdev mdl3-models`.

| Off | Field |
|---|---|
| +00 | f32 bounding sphere radius |
| +04 | f32 x3 bounding sphere center |
| +10 | u16, always 0 |
| +12 | u8 flags |
| +13 / +14 | u8 bounds point count (8 = box corners) / offset of float3 array (PDTools reads +12 as a u16 count: 0x0408 = flag 4, 8 points) |
| +18 / +1C | command stream offset / size |
| +20 / +24 / +28 | VM instruction pointers, -1 when unused (+20 and +28 always -1 in cars) |
| +2C | u16, mostly 0xFFFF |
| +2E | u16 flags |

**Command stream** (`Mdl3Command.cs`, opcodes and operand sizes from PDTools): it is a small program that draws
shapes. The decoder consumes **exactly** the declared size for every one of the 46,578 car models, every jump
lands on a command boundary, every shape index is in range, and **every shape of every car is drawn by at least
one model**. So the command stream is the complete model -> shape map.

- `op59 <u8 shape> <u8>` draws one shape (by far the most common); `op60 <u16 shape> <u16>`, `op3`/`op4` the same with other operand sizes; `op74`/`op75` draw a list.
- `op5 <u16 value> <u8 n> <n x u16 rel>` is a switch; `op9`/`op10` relative jumps (relative to the end of the operand).
- In car 00010007 the switches choose between day/night shapes (`GT_DAYTIME` / `GT_NIGHT`), brake lights on, retractable headlights open/closed.
- The command 8 reader in PDTools reads `flag & 0xF` twice (floats, then shorts); it looks like a bug, but the data agrees: all 45,522 op8 in GT6 decode exactly.

Opcodes used by cars: v8 `0 5 6 8 9 10 14 15 17 18 37 44 51 53 59 60 62 70 71`; v14 adds `4 54 61`.

## Shapes (header 0x14 count, 0x38 offset) — layout verified, data not yet

0x30-byte records (`Mdl3Shape.cs`). This is the "mesh table" of the earlier research.

| Off | Field | v8 (GT5) | v14 (GT6) |
|---|---|---|---|
| +00 | u16 flags | 36 values | 39 values |
| +02 | i16 fvf index | | -1 on packed shapes |
| +04 | i16 material index | | |
| +06 | u16 | always 8 | always 0 |
| +08 | u32 vertex count | non-zero in 24% | non-zero in 20% |
| +0C | u32 vertex data offset | set when count is set | **always 0** |
| +14 / +18 | u32 tri length / tri offset | | +18 always 0 |
| +20 | u32 | 0000FFFF | 0 |
| +24 | u16 | 0/1 | FF00 on packed shapes |
| +26 | u16 triangle count | | |
| +28 | u32 bbox offset (8 corners) | always set | always 0 |
| +2C | u32 packed mesh reference offset | set in 84% | set exactly when vertex count is 0 |

- Triangles of plain shapes: u16 indices at +18 (absolute), +14 = index count. In GT5 the **low nibble of +00 is the
  RSX primitive**: 5 = triangle list (+14 = 3 x +26), 6 = triangle strip with the restart index of +20 (0xFFFF),
  degenerate triangles skipped, winding flipped on odd positions of each strip (1755 GT5 tire shapes; the non-degenerate
  count = +26 on all). GT6 plain shapes have nibble 1 or 9 and are all lists.
- Empty shapes (0 vertices, 0 triangles, +26 0, never drawn): 5128 GT5 tire `*_side` templates, 265 GT5 wheel shapes.
- In GT6 a shape is either plain (vertex count set, offsets 0: the data must be in `body_s`) or packed, never both.
- In GT5 most body shapes are packed (all of them in car 00010007 hq): see "GT5 packed geometry".
- **S1 positions** (wheel/tire `Shadow` formats, some `Flex` tires): signed 16-bit fixed point, position = raw / 2^k
  with k = 16 on most shapes, 15 or 13 on others (big tire templates). k is not in the shape or the FVF (two FVFs with
  the same bytes have k 16 and 13); where it is stored is open (maybe a material constant). The decoder takes the k
  (12..17) for which most axes match the v8 shape bbox size within 1% (some boxes are loose on one axis): 0 shapes out
  of the box after that. GT6 has no shape bbox: k = 16 (9 GT6 shapes, tires/carparts). Tire 0134 (Schwimmwagen menu)
  has a non power of two scale on Y and Z against its box: still inside it.

## Vertex formats (FVF, header 0x18 count, 0x40 offset) — verified

`Mdl3Fvf.cs`, checked with `gtdev mdl3-fvf` on all car files (GT5: 95,709 formats, GT6: 23,922).

Record 0x78 bytes: +00 name offset (`Flex`, `SimpleFill`, ...), +04 u32, +08 element list offset,
+14 offset of 0x10 bytes (GT5 only, 64% of the formats): one RSX vertex attribute slot per element, zero after the element count,
+18 u8 element count, +19 u8 vertex size, +74 array definition offset. Element 8 bytes: u32 name offset, u8 start, u8 component count,
u8 RSX type, u8 array index.

RSX types (`CELL_GCM_VERTEX_*`): 1 S1 normalized short, 2 F float, 3 SF half, 4 UB normalized byte, 5 S32K short,
6 CMP 11/11/10-bit vector in a u32, 7 UB256 byte.

Checks, all files: no element runs past its vertex, no two elements overlap, and in every array of every format
the element sizes add up to the vertex size **exactly** (no unused bytes). So the vertex layout is fully known.

| Attribute | Types seen | Status |
|---|---|---|
| `position` | always F x3 | **verified**: for all 57,043 GT5 plain shapes every vertex is inside the shape bbox (26,141 bboxes are exact min/max) |
| `normal` | CMP | **verified**: x = bits 0-10, y = 11-21, z = 22-31, signed, /1023, /1023, /511 -> 99.9% of 3.26 M normals have length 1 +-0.01 (the other bit order: 5.6%) |
| `tangent` | S1 x4, UB x4 | layout only |
| `map1`, `map12` (two UV sets), `map34` | F, S1, UB x2/x4 | **open**: S1 stays in -1..1, F goes up to +-500: probably scaled by material/shader constants (item 2e) |
| `colorSet1`, `colorSet2` | UB x4, SF x4 | layout only |
| `blendIndices`, `blendWeight` | F x4 | GT6 only (skinning) |

**Extra arrays (morphs).** Elements with array index 1 and 2 (`position` + `position_2`, `normal` + `normal_2`, ...)
are two full copies of the vertices. Address of vertex v: `vertices + arrayDataOffset + arrayElementSize * index + v * arrayVertexSize`
(array definition, 0x10 bytes: u32 data offset, u32 length (2), u32 element size = vertex count * vertex size, u8 vertex size, u8 0x10).
Used by `hi_low_brend_model` (hi/low LOD blend) and by moving aero: Enzo rear wing, Quattro wing, front wings, spoilers.

Plain vertex buffers: `shape.VerticesOffset` is absolute in the file; vertex v at `VerticesOffset + v * vertexSize`.
In GT6 no car body shape has a plain buffer in the MDL3 (offset 0): the data is in `body_s`.

## Materials, shaders, texture set (header 0x44 / 0x4C / 0x48) — verified

`Mdl3Material.cs`, checked with `gtdev mdl3-matcheck` on all car files (GT5 hq/race: 2176 files, 1,066,614 materials;
GT6 hq/race body: 2454 files, 1,275,988 materials): no check fails.
Research helpers: `gtdev mdl3-mat` (column stats, or a dump of one file), `gtdev mdl3-uv` (UV ranges vs textures).

**Material block (0xE4, 0x18 bytes)**: u16 material count, u16 material data count, u8 (meaning open), u8 render state count,
u16 texture info count, then offsets of the four tables. The tables are contiguous: materials, material data,
(other data), render states, texture infos, and the `TXS3` header starts right after the last texture info (gap 0 in every file).

| Record | Size | Fields |
|---|---|---|
| material | 0x34 | +00 name (may be 0), +04 i16 material data index, +06 i16 render state index (always < state count), +08 u16 flags (GT5 1/3, GT6 1/3/9/B), +0A u16 sampler key count, +0C sampler keys offset. **+10..+33 always 0** (runtime space) |
| material data | 0x28 | +00 shader family name (`Flex`, `SimpleFill`...), +04 index, +08 u16 0x0102 (GT6 also 0x0002), +0A constant count, +0C vec4 values, +10 constant keys (name -> vec4 slot; max slot + 1 == count in every record), +14 shader reference, +18 list (count u16 +22) of (u32 n, u32 offset of n u16), +1C u16 pairs (count u16 +20; offset 0 iff count 0), +24 == +0C of its shader reference (= its pass list) |
| shader reference | 0x28 | +00 same name as the material data, +04 SHDS definition index (always in range) |
| render state | 0xA0 | RSX command buffer, see below |
| texture info | 0x44 | RSX command buffer + image index + name, see below |

- **Sampler keys** (8 bytes, sorted by name, inside the textureKeys table at header 0x90): `diffuseMapSampler`, `alphaMapSampler`,
  `specularMapSampler`, `reflectColorShiftMapSampler`, ... -> texture info index. **Bit 15 set = external texture**
  (`0x8000|n`): not in the file, provided by the game (mostly `reflectColorShiftMapSampler`, and in GT6 `scatterColorShiftMapSampler`
  and many others: paint/colour-dependent textures). All other indices are < texture info count.
- **Shader constants** are named vec4s: `diffuseColor0`, `diffuseColorM`, `specularColor0`, `shininess0`, `envFresnel0`,
  `reflectivity0`, `emissionColor`, `lensUVShift`, `mipmapShift`, `paraboloidMatrixIT0..2`, `nLoop`, `radius`, ...
  There is **no UV scale constant** among them.
- **Render state** = RSX method stream: `0x00780304` (30 registers from 0x304: alpha test enable/func/ref, blend enable,
  blend factors, blend colour, blend equation, colour mask, stencil front/back, shade mode, MRT colour mask, logic op),
  `0x000C0A6C` (depth func, depth mask, depth test enable), `0x0004183C` (cull face enable), then `0x00020000`, `FFFF0000`.
  The values read as expected GL enums (0x207 ALWAYS, 0x302/0x303 SRC_ALPHA/ONE_MINUS_SRC_ALPHA, 0x8006 FUNC_ADD, 0x1E00 KEEP, 0x1D01 SMOOTH, 0x1503 COPY).
- **Texture info** = RSX method stream `0x00201A00` (8 registers from 0x1A00: offset (0 on disk), format, address/wrap,
  control0, remap, filter, image rect, border colour), `0x00041840` (control3: depth << 20 | pitch);
  GT6 adds `0x00040B00` (control2, always 0x2DC8) at +2C. +3A u16 = TXS3 image index, +40 = texture name.
  On every file: width/height, format (FORMAT bits 8-15) and mipmap count (bits 16-23) equal the TXS3 image record.

**SHDS (0xFC)**: +08 = 0xFC (own offset, reloc base), +16 u16 definition count, +18 u16 vertex program count,
+1A u16 fragment program count (the "second program table"; its index is in the definitions), +1C definitions (0x0C: name, u32 id,
i16 vertex program, i16 fragment program, both always in range), +20 vertex programs (0x28: microcode offset, size
(multiple of 16 = RSX VP instructions), +08 u16 count of a +10 table (0x10, named), +0A input count, +0C constant count,
+14 inputs (0x10 each, named), +18 constants (0x10 each, named, register numbers near 467 = top of the RSX constant file)),
+28 = MDL3 header 0x6C, +2C fragment programs, +30..+40 three more tables (see 2f below).
GT6: definition count == material data count (2449 of 2454 files); GT5: more definitions than material data.

**TXS3 header in MDL3** (0x40): +08 = own offset (reloc base), +10 0 (GT5) / 1 (GT6), +14 texture info count **0**
(the infos are in the material block), +16 image count, +1C image records right after the header (0x20 each:
offset, size, format, mipmaps, width, height), +20 = MDL3 header 0x6C.
- GT5: image offsets are absolute in the MDL3; most images start at header 0x6C, big ones sit right after the
  image record list. The data after the last image is the **fragment program microcode** (SHDS +40, see 2f).
- **GT6: 168,414 of 169,980 images have offset 0** — the pixels are not in `body` (a 512x512 DXT5 is bigger than the file).
  To find with item 3 / item 5 (`body_s` or another file).

### UV scale (the open point of 2d) — answered for GT5 plain shapes

`gtdev mdl3-uv` over the 2176 GT5 car files (all plain shapes, every vertex), UV decoded as S1/32767, UB/255, F as is:
- S1 and UB UVs are inside 0..1 (except tiny overhangs like -0.0002 and degenerate one-point shapes);
  shapes "0..0.5" are quadrants of 2x2 atlases (the shapes next to them cover the other quadrants with a half-texel inset).
- Only F UVs go past 1 (up to 800): of the 1,784 F shapes outside 0..1, 1,769 sample textures with **wrap = repeat**
  (only 15 use clamp on every texture): they are tiled textures.
- So there is **no UV scale**: the decoded value is the texture coordinate, and V is not flipped by a constant.
  (PDTools' note about a scale "in the shader programs" may concern GT6 packed meshes, whose packed elements have their own
  bit layout and scale: item 4.)

## Roadmap 2f: byte coverage of the whole file

`gtdev mdl3-coverage <files.tsv> <groupRegex> <out.txt>` (or `mdl3-coverage <file> <out.txt>` for one file with the list of
unclaimed runs and of claims). Every structure below *claims* its bytes; a byte claimed by two different structures is
reported as an overlap (a layout error), and what nobody claims is counted per header region. Table links and counts are
checked in the same pass. Run on 16,808 files: car body (GT5 `car\hq|race`, GT6 `car\...\hq|race\body`), interior
and wheel files of both games (`tests\mdl3_tables\coverage_all.txt`):

| | GT5 v6 (841) | GT5 v8 (9815) | GT6 v14 (6135) |
|---|---:|---:|---:|
| bytes claimed | 9,354,837 | 6,360,664,282 | 2,439,262,461 |
| unclaimed, zero | 463,786 | 256,740,280 | 29,496,899 |
| **unclaimed, non-zero** | 2,599,233 | 1,845,407,982 | **0** |
| overlaps | 0 | 0 | 0 |
| failed checks | 2 (0x78) | 31 (0x78) | 0 |

- GT6: **every byte of every car, interior and wheel file is explained** (non-zero bytes all claimed, no overlap, no failed check).
- GT5: all unclaimed non-zero bytes but 11 are in the shapes region: the **GT5 packed geometry** (item 4).
  The other 11 are in GT5 `car\race\00040079`, the file that is 0x40 bytes short: they sit in the 0x40 padding after its
  last fragment program microcode and look like one more FP instruction (the file is cut).
- The 33 failed checks are 34 stale 0x78 records (see 0x78).
- Not covered: 17 old files (versions 1, 2, 4 in GT5 `car\interior` and `wheel`): their layout is different (header fields
  move), out of scope for the car work.

### Named float blocks (0x54, count 0x2A)

u32 pointers to blocks `u32 name, u16 tag, u16 n, n x f32`.

| Name | Tag | n | Meaning |
|---|---:|---:|---|
| `LightMaskSH` | 5 | 75 | spherical harmonics coefficients (25 x RGB) |
| `InteriorSH` | 6 | 75 | same for the interior |
| `InteriorSHAdjust` | 7 | 3 | |
| `LightMaskCenter` | 610 | 3 | a point |
| `occlusion` | 600 | 12 x k | k quads (4 corners x xyz) |
| `GT_INTERIOR` | 113 | | |
| GT6 `Front/Rear Normal/Option` | (junk) | 9 | |

### VM: host registers, outputs, bytecode, context (0x58, 0x5C, 0x60..0x68, 0xBC)

The model VM of PDTools. Opcodes and operand sizes from PDTools; operands are **little-endian** (the rest of the file is big-endian).
- **0x58 (count 0x1E)**: host registers the game writes, 8 bytes `u32 name, u16 array length (1), u16 register index`, sorted by name:
  `lightMode`, `taillamp`, `passing`, `wiperFront`, `meter_rot0`, `custom0`... Indices are unique; 0..n-1 except 14 GT6 files with gaps.
  `reval`/`rpop` with bit 15 set read/write these registers.
- **0x5C (count 0x22)**: VM outputs the game reads (`wiper0_00u`...): `u32 name, u32 0x10000 | slot`, slot < VM stack size (header 0x20).
- **0x64**: bytecode; **0x60 = code length**, 0x68 = end of the VM block (code + storage, zero on disk).
  Model +24 and VM context +00/+04 are entry points into it.
- **0xBC**: VM context, 0x20 bytes: +00/+04 entry points, +08 u16 curve count (+0A u16, set in GT5), +0C curve list
  (8 bytes: u16 type 1, u16 point count, u32 offset of (x, y) f32 pairs), +10/+14 always 0x9A1BC4 / 0x18FD74 in GT5, 0 in GT6.

### Per-material runtime records (0x78, count 0x26)

0x14 bytes: u16 material index (strictly increasing), u16 n, 12 zero bytes, u32 offset of n x 12 bytes (zero on disk: runtime space).
What n counts is **open** (it is not the sampler count, shape count, draw count, pass count or animation row count).
Stale records, GT5 only: 34 records in 33 files (car `hq\00010101`, the rest wheels) have material index == material count
(once count + 1), 33 of them as the last record: probably materials deleted after the table was built. Never in GT6.

### Shader references and passes (0x88, count 0x2C)

0x28 bytes: +00 name (same as the material data), +04 SHDS definition index, +08 0, +0C passes (u16 count +10),
+12 u16 = 1 exactly when +14 is set (GT5 only), +14 offset of 8 bytes (u32 pointer to its own +04, u16, u16),
+18 constant key count, +1C constant keys, +20/+24 zero.
Pass, 0x20 bytes: u16 n0, u16 n1, u32 list0 (n0 x 8, offset 0 iff n0 is 0), u32 list1 (n1 x 8), u32 SHDS vertex program record,
u32 SHDS fragment program record (both always point exactly at a record), u16 x4, u32 0.
Material data +24 == the pass list of its shader reference.

### SHDS fragment programs and the +30..+40 tables

- **+2C fragment programs** (count +1A), 0x48 bytes: +00/+04 offset/size of an FP header block, +08 u16 count of a +10 table,
  +0A u8 count of +38 (global samplers), +0B u8 count of +14 (samplers), +0C u16 count of +18 (constants); all four tables have
  0x10-byte named entries.
- **+38** (count +30): 0x10 records: u16, u16 name count, u32 list of name pointers, u32 sub record
  (0x10: u16, u16 length A, u32 data A, u32 data B (4 bytes)).
- **+3C** (count +32, == material data count in 4575 of 4630 files): pointers to records of 0x27 + n bytes
  (u32 name, u32, u32 n, u32 offset == record + 0x27, then the n bytes).
- **+40** (count +34): 0x20 records: u32 = the +00 of some FP record, u32 offset / u32 size of the **FP microcode**
  (size a multiple of 16; in GT5 it sits after the last TXS3 image), rest zero.

### Model material lists (0xB0, 0xB4)

One 0x40 record per model: +00 offset of a u16 list (count +30) that equals, in every model of every car file,
**the sorted list of materials of the shapes the model's command stream draws**. +04..+27 zero; +28/+2A/+2C/+2E u16 (open).
0xB4 == the models offset.

### Material parameter animation (0xB8)

Header 0x20: u16 n4, u16 n8, u32 params, u32 curves, i32 x2 VM pointers (-1), u32 +14 table, u16 n14, u16 binding count
(== max binding index + 1), u16.
- param 0x10: u32 name, i16 VM storage index or -1, i16 row count, u32 rows, f32 default value;
- row 0x10: u16 material data index, u16 constant slot, u16 binding index, u16 (mostly 0, up to 11), 4 x u16 curve (0xFFFF none);
- curve 8 bytes: u16, u16 point count, u32 offset of (x, y) f32 pairs (piecewise linear);
- +14 table: n14 named 0x10 entries (`rcs`, `rcs2`).
So a VM value (or a host input) drives a curve that writes a material constant: brake light glow, meters, lamps.

### Joints, rigid bodies, soft bodies (0x98..0xA4, 0xC0)

- **0x9C (count 0x98)**, the "wing data" of PDTools: **joint constraints**, 0x80 bytes, angle limits (+-2 pi, +-pi/2);
  keys at 0xA0 (count 0x9A) name them: `BASE`, `steering`, `shift_H`, `wiper_R`...
- **0xA4 (count 0x28)**: pointers (then padding to 0x80) to 0x280-byte **rigid body** blocks: box min/max, inertia tensor,
  parameters, 8 corners, an edge list, constant 0x00800630.
- **0xC0 (count 0xC4, GT5 only, every car file)**: pointers to **soft body (FEM damage)** blocks. Header 0x40: u16 node count,
  u16, u16, u16 tetrahedron count, u16 1, u16 triangle count, u16 n6, u16 n7, u32; offsets of nodes (vec3), node values (f32),
  tetrahedron matrices (0x1B0 each), constants (0x50), triangles (u16 x3), a u16 list (n6); +3C == own offset.
  Header 0xC6 == 0xC8 == 0x124.

### GT5 geometry block (0x94, no count, v8 only)

Header 0x34: u16 n1..n5, ...; p1 (n1 x 0x20), p2 (n2 pointers to 0x20 records), p3 (n3 x 0x20, +04 size / +08 offset of a stream
description with quantization floats), p4 (n4 == n1 lists of RSX attribute formats, e.g. `01 00 14 32` = attribute 1, stride 0x14,
float x3), p5 (n5 == n3 lists of 12-byte entries), p6 (f32 1.0), p7 runtime space.
Together with the shape packed references (shape +2C, 0x40: 12 f32, u32 offset, u32 index) this describes the GT5 packed
geometry (PS3 Edge style). **Decoded in item 4, see "GT5 packed geometry" below.** p3 = one stream description per packed shape.

## GT5 packed geometry (item 4b/4c) — verified

`GTFormats/Mdl3/Mdl3Packed.cs`, checked by `gtdev mdl3-packed` on every GT5 car\hq, car\race, car\interior and tire file
(204,792 packed shapes, 276,794 segments, 0 exceptions; GT5 wheels have no packed shapes). Exported by `gtdev mdl3-obj`.

**Chain.** A shape is packed when its vertex data offset is 0 and shape +2C points to a 0x40 record whose +30 is set.
- 0x40 record: +00..+2F 12 floats (0 on body shapes; plain shapes with a record carry values like 1.0 / 0.0008 here), +30 -> **H**.
- H: +04 -> L, +0C u16 segment count, +0E u16 (0x35, 0x39, 0x4C...).
- L: u16 1, u8, u8 flags (0x01, 0x25), +04 -> M, +08 -> 0x14 bytes.
- M: u16 1, **u16 stream description index X** (p3[X]), u16 Y (2 x running segment number), u16 segment count, +08 -> list of segment pointers.

**Segment record S** (0x50 bytes, more with the optional blocks):

| Off | Field |
|---|---|
| +00 | -> stream list: per stream u32 offset, u32 -1 |
| +04 | -> index info, one 0x10 entry per LOD: u16 triangles, u16 0, u16 index data size, u16 0, u32 offset, u32 -1 |
| +08 | -> list of (LOD count - 1) extra streams (only with LODs; look like vertex -> parent pairs for geomorphs, open) |
| +0C | -> optional extra block (73,741 hq shapes have one; u16 lists, maybe skinning/blend data, open) |
| +1C | u32 record size (0x50, 0x9C, 0xD0) |
| +20 | u8 stream count (1 or 2), u8 1, **u8 LOD count**, u8 extra stream count |
| +24 | -> count block, one 8-byte entry per LOD: u16 kept, u16 added, u32 LOD count. LOD k uses the first kept + added vertices; kept(k) = kept(k+1) + added(k+1) |
| +28 | -> position format (0 when positions are F32 in stream 0) |
| +2C | u16 position format size (0, 0x10, 0x34), u16 (5, 0x0A, 0x0D...) |

**Position format** (0x10 or 0x34 bytes): +08 `01 63 00 10` or `01 03 00 24`, **+0C/+0D/+0E bits per axis** (8..16),
+10 six floats (= the car frame, 0x34 only), **+28/+2C/+30 u32 grid offsets x 4** (0x34 only).
Positions are a MSB-first bitstream in the last stream: per vertex x, y, z with those bit counts.
- grid = raw + offset / 4: every segment of a car maps into **one 16-bit grid per car** (raw + offset never passes 65535).
- **position = grid x F / 2^21 + (F + G)**, with F = the first float triple and G = the second (the car frame, the same in
  every stream description and position format of a file; per car, 903 different sets in GT5 hq).
- Verified exactly: on 8 GT5 shapes that also exist as a plain twin (same name and triangle count) every vertex lands on the
  plain vertex within 2e-5 m (half a grid step); all 204,792 shapes have every vertex inside their shape bbox
  (2 race shapes: 1 vertex each just outside).

**Stream description** (p3[X], one per packed shape): u16 size, u16 flag; flag 8: u16 output stride, u16 0, input layout,
output layout; flag 4: one layout (input = output, positions F32 inside). Then the car frame floats when the size leaves room.
Layout: u16 attribute count, u16 stride, 4 bytes per attribute: u8 id, u8 type << 4 | count, u16 byte offset.
- Types: 0 F32, 1 F16, 2 I16N (/32767), 4 X11Y11Z10N (like RSX CMP: x bits 0-10, y 11-21, z 22-31; 99.97% unit length), 5 U8N.
- Ids (matched with the shape FVF names): 01 position, 02 normal, 03 tangent (I16N x4), 05 texcoord (2 components: map12
  or map1, map34 when map12 has 4), 11 second texcoord, 90/91/92 the remaining FVF elements in order
  (colorSet1 when U8N/F16 x4, map12 x4 when I16N/F32 x4).
- The output layout does not follow the shape FVF offsets (the FVF has the position first); output stride == FVF size in
  all but 22 car shapes and the tire formats.
- Stream 0 = the input layout (stride x vertex count, padded to 16); stream 1 (two-stream segments) = the position bits.
  Single-stream segments: either all attributes with F32 positions, or only the position bits.

**Triangles** (Edge-style compressed indices): header u16 bits per value, u16 value count n, u16 n again, u16 0; n values
(vertex indices, MSB first); then **from the next 2-byte boundary** one 2-bit code per triangle:
`3` = three new values, `0` = (a, c, new), `1` = (c, b, new) from the previous triangle (a, b, c); `2` never occurs.
- Every value is used exactly once, every index < vertex count, 0 degenerate triangles; winding is consistent
  (no directed edge used twice except where the data repeats a triangle, as plain shapes also do).
- **Index data size = 8 + ceil(n x bits / 8) + ceil(triangles / 4) + 2** (the 2-byte alignment pad is not counted): exact on all segments.
- LODs (race models, some hq/interior): each level has its own index data on the same vertex buffer (first kept + added
  vertices); 96,701 levels check like LOD0; the last level is often empty (0 triangles, offset 0). 5 last levels index
  past their count (open).

**Shape +26 vs the segment triangle sum**: equal for 86,185 of 86,661 hq shapes (+26 is u16 and wraps on huge shapes).
Where it differs the sum is always lower (by 1 .. 2588; every tire is 1 lower): likely triangles the Edge partitioner
dropped (degenerate in float positions). Open: check for holes.

### Export notes (item 4c.1)

- **Models of a car file** (GT5 hq/race): `GTBE_BODY` (the car), `GTBE_INTERIOR_SHARE`, and helper models that copy
  body shapes: `GTBE_SHADOW_BOX`, `GTBE_SHADOW_PLANE`, `GTBE_SS_BODY` / `GTBE_SS_SHADOW_BOX` (self shadow, `GT_SS_AUTOTIRE`
  cylinders), `GTBE_LIGHT_MASK`, `GTBE_ILLUMINATION`, `GTBE_PROJECTION`. Exporting all of them overlaps the same surfaces.
- **LODs**: hq files are `GT_LOD|LOD_H`, race files `GT_LOD|LOD_L` (whole-car levels in separate files); packed segments
  add progressive levels (see above). Low levels reuse the LOD0 vertex normals on the kept vertices: `gtdev mdl3-normals`
  on GT5 race, triangles agreeing with their vertex normals 99.55% (LOD0), 97.2, 95.1, 93.3, 89.7% (LOD4); directed edges
  used twice 0.09% -> 1.55%, so the winding stays consistent and the drop is the simplification, not the decoder.
  Plain `Flex`/`SimpleFill`/`Shadow`/`WheelBlurShadow` formats have no normal at all (decals, shadows).
- **Part transforms**: `GTM_SUSPENSION_FL/FR/RL/RR` parts (brake discs, calipers) are in hub space (discs of all four
  wheels at the same place). The command stream wraps them in `op14` (push) / `op15` (pop) with `op6 <u8>`
  (0x0F..0x12 per wheel, 0x24..0x27 for the disc): engine matrix slots, not bones (00010075 has 1 bone, no host registers).
  The wheel positions are not in the MDL3.
- **Bones** (header 0x1A count, 0x50 offset, 0x48 bytes, `Mdl3Bone`): matrix, name, parent, id (`steering`, `_tmp_joint_nameN`).
- `op5` switch: u16 value, u8 case count, relative targets. In 00010075 (no host registers) the value is 7 around the
  day/night shapes and 0x0B..0x0E around the four caliper groups: engine state slots (meaning open).

### PMSH (0xD0, v14) and packed mesh keys (0xCA/0xCC)

Header 0x40: `PMSH`, +04 u16 2, u16 flag, +08 own offset, +0C == header 0x6C when the flag is 1, +10 mesh count, +12 0x1C count,
+14 layout count, +16 declaration count, +18 meshes, +1C entries (8 bytes: u8, u8, u16 data size, u32 data offset; 39 MB of data
over the GT6 cars), +20 bit layout arrays (u32 count, u32 offset of count x 0x30), +24 declarations (0x10: u8 packed element
count, u8 raw element count, u8 packed stride, u8 raw stride, u32, element list (4 bytes each), name list), +30/+34 0x20 records.
Shape packed ref +34 = mesh index, or -1 (GT6 interiors/wheels).

**Mesh** (0x30): +00 i16 declaration, +02 i16 bit layout array, +04 i16, +06 u8 smooth data count (s), +07 u8 (k, 1 or 8),
+08 s x k x 8 bytes, +10 s smooth mesh data (0x30 each), +14 s bboxes (0x60 = 8 corners each).
In GT6 **car bodies** the smooth data offsets are 0 (the data is in `body_s`); in **interiors and wheels** it is in the MDL3.

**Smooth mesh data** (0x30, PDTools `PMSHSmoothMeshData`; the sizes below are exact on all 6135 GT6 files):
| Off | Field | Size of the data |
|---|---|---|
| +00 | packed vertices | sum over the layouts of ceil(V x bits / 8) |
| +08 | 10-byte entries | +24 x 10 |
| +0C | raw vertices | V x declaration raw stride |
| +10 | +28 quadwords, then per-vertex streams: 1 byte (flag 0x40), 4 bytes (flag 0x80) | +28 x 16 + pad16(V) + pad16(4 V); the padding is not always zero |
| +14 | packed indices | +2A x 16 |
| +18 | smooth (tessellation) commands | +2C x 16 |
| +20 | u16 flags (0x40, 0x80, 0x20, 0x04, 0x200, 0x8000 seen) | |
| +22 | V = vertex count of both vertex arrays | |
| +26 | output vertex count, >= V (larger when the SmoothMesh job adds vertices; PDTools calls it the raw count) | |
| +2E | u8 index bit size | |

**Decoding (item 4d)** — `Mdl3Pmsh.Decode`, `Mdl3PmshCommands`, checked by `gtdev pmsh-check` on every GT6 interior,
wheel, tire and carparts file with the data in the MDL3 (4717 files, 61,397 data blocks, 53.6 M vertices, 82.5 M triangles):
0 exceptions, 0 positions outside the block box, 99.73% of triangles agree with the stored normals, 0.126% directed
edges used twice; 3 interior blocks leave some vertices unused (open). Visual check: Demio wheel (`gtdev mdl3-preview`)
and tire t0256 whole; Demio interior checked by the user in ZModeler.
- Shape packed ref +34 = PMSH mesh index (also in interiors/wheels: the 4 wheels share the same data blocks).
- **Packed elements**: one bit array per layout, V values each, MSB first, layouts in declaration order;
  value = (raw / (2^(bits-1) - 1) + 2) x scale + offset (PDTools). The packed CMP normal has no layout of its own
  (see Normals); colorSet1 and colorSet2 together take one; colorSet1 alone has one only with data flag 0x40;
  map34 shares the 4-component layout of a 2-component map12 (counted on map12, map34 = its 3rd and 4th components).
  Position is always the first element. A 3-component colour layout has the alpha in offset[3] (1).
- **Raw elements**: V records of the raw stride at +0C, element at its output offset (normal CMP, tangent, colours, UVs).
- **Normals**: raw CMP element; else, with a packed CMP normal, the **4th component of the position layout**
  (32 bits, scale 1, offset 0: the raw bits are the CMP word); else the 4-byte stream of flag 0x80 (+10 after +28
  quadwords and the 1-byte stream of flag 0x40); else not stored (declarations without a normal: we use area-weighted
  face normals). With colorSet1 + colorSet2 the 4-byte stream is not a normal (same word on very different normals):
  reading it as CMP gave 95% non-unit normals on GT6 bodies before the position W rule.
- **Block box** (mesh +14, 0x60 per block): two groups of 4 corners stored as x[4], y[4], z[4] (oriented box).
- **Commands** (+18, MSB first, 5-bit opcodes, operand widths from PDTools): the job keeps a 2^idxBits vertex ring;
  `op5 slot, groups (first vertex, count - 1)` loads mesh vertices into consecutive slots; `op1 n` draws 4 n
  triangles (idxBits per index, read in sequence from +14; the last group of 4 padded with (0,0,0)).
  Loads are **double-buffered**: the op5 of the next batch comes before the current batch draws, and batches read
  slots of earlier batches too, so a load takes effect at the next `op14` (batch start: first new slot, count - 1).
  An op5 holds at most 15 groups: a longer load continues in the next op5 at the slot where the full one ended.
  Applying loads at once breaks the later batches (Demio wheel: big wrong triangles from batch 7 on).
  `op15` holds a nested list (SmoothMesh cage) where `op2 n` draws 4 n quads (4 indices, split (a,b,c) (a,c,d),
  d == c or d == a = triangle). The outer list of SmoothMesh blocks (op7/9/10/11/12/21/22/23) holds the
  subdivision patch data (not needed for the cage). Every vertex used and the index data consumed up to its last
  quadword on all but 3 blocks.
- Open: the 1-byte stream (flag 0x40), the 4-byte stream when the normal is in the position W, +08 10-byte entries,
  output count > V (vertices added by the job), several data blocks per mesh (merged for now), data flag 0x01
  (layout count 1 = position only, positions outside the box: 11 race blocks).

**GT6 car bodies** (`Mdl3BodyS`): the shape data is in the `body_s` bundles (see the stream map below). Each bundle's
shape table record (0x20) starts with u32 shape index; the first bundle holding a shape is used.
- PMSH shape: +04 0, +08 0, +0C box (8 corners x, y, z, 0x60), +10 0x10 bytes (the mesh +08 records), +14 offset of
  the smooth mesh data records (0x30 each, same layout as above, every offset relative to the bundle), +18 the box
  again as x[4] y[4] z[4], +1C data record count.
- Plain shape (e.g. `GTBE_OCCLUSION`): +04 vertices, +08 triangles (offsets in the bundle), +0C box; the vertex
  format, counts and triangle length are the MDL3 shape's.
- `gtdev pmsh-check` on all 2454 GT6 car bodies (hq + race): 267,778 data blocks, 142 M vertices, 206 M triangles,
  0 exceptions, 99.47% of triangles agree with the normals, 0.04% normals off unit length, 0.210% directed edges used
  twice. Failures: 14 blocks with unused vertices, 11 flag 0x01 blocks (layout count 1, positions outside the box).
- Interiors/wheels/tires/carparts with the position W rule: 99.73% agreement (was 99.44%), 6805 blocks without a
  stored normal (was 12,554).
- Demio (car\0001\0112\hq\body): 200 of 200 exported shapes, 281,093 triangles; the thin line above the windscreen
  is the real roof antenna.

**Packed mesh keys** are not mesh names: each is (shader family name, material data +04 index) of an existing material data.

### Shape stream map (0xDC, v14): the link to body_s

Header 0x20: u32 group count, u16, u16 2, u32 groups. Group 0x0C: u32 name (`/`, `custom_brake0`, `custom_caliper0`,
`custom_a0`..`custom_f0`), u32 n, u32 items (0x18: a stream + u32 m + u32 sub streams (m x 0x10)).
Stream 0x10: u32 0, u32 **offset in body_s**, u16 c1 header chunks, u16 c2 data chunks, u32 chunk list
((c1 + c2) x 8: u32 stored size, bit 31 = raw deflate (size < 64 KB), else stored as is; u32 inflated size).
`GTFormats/Mdl3/Mdl3StreamMap.cs`, checked with `gtdev body-s` on all 2454 GT6 car bodies (0 failures, `tests\txs3_body`):

- The streams **tile `body_s` exactly**: sorted by offset, each starts where the previous ends, the last ends at the file end.
  The chunks of a stream are contiguous from its offset, header chunks first; every chunk inflates to exactly its size.
- The chunks of one stream, inflated and concatenated, form a **bundle**. Bundle header (big-endian): +00 0, +04 header size
  (= sum of the header chunks), +08 bundle size, +0C data size, +10 0, +14 u16 shape count, +16 u16 image count,
  +18 shape table (0x20 records, u32 shape index first), +1C image table (0x10 records: u32 TXS3 image index,
  u32 pixel offset in the bundle, 8 zero). The map pads the size of a deflated last data chunk to 0x80
  (5 race bundles; the bundle header has the exact size).
- Empty bundles (0x80 header only) are common (`/` sub streams without own data, empty `custom_*` items).
- `/` has one item = the car; its sub streams hold almost only `ReflectColorShiftBRDF` / `ScatterColorShiftBRDF` images:
  most likely one per paint colour (to confirm with the colour list). `custom_*` items are the alternative parts
  (brakes, calipers, mufflers ...), each with its shapes and images.
- Shape data (unclaimed non-zero bytes) is in the header part, and in the data part of 30 bundles that have shapes:
  decoding it is item 4/5. Images: see `docs/txs3.md`.
- PDTools reads a different streaming table (header 0xAC, 0 in every GT6 car file) and does not read images from `body_s`.

### Item 5a: the `/` sub streams = colour variations

`gtdev bodys-subs <files.tsv> <groupRegex> <out.txt>` (all 2454 GT6 bodies, ~1 min, `tests\body_s_subs`; also a
`.tsv` with one line per body) and `gtdev bodys-subs <body>` (one car: every sub stream, its images with a pixel hash and
the materials that sample them).

- GT6 materials that change with the car colour are named `<material>_CLR_VARI_<k>` (GT5 has no such names). V = the
  number of variations (highest k + 1; k is contiguous except on 8 bodies).
- The `/` item has always **2 x (V + 1)** sub streams (all 2035 bodies with CLR_VARI materials, except 17 with V = 1 and
  no sub streams); 402 bodies without CLR_VARI have none. hq and race of the same car agree (1222 of 1227; 5 race
  bodies have none).
- The sub streams form two halves of V + 1 slots. Slot k (k < V) holds the images of the `_CLR_VARI_k` materials:
  12,802 of 13,461 non-empty slots are sampled by `CLR_VARI_k` (alone in 10,779); the other 659 are 527 half A slots
  holding copies of images of other variations and 132 half B slots, mostly the last slot (below). Images used by
  several variations are copied into each slot (identical pixels, 3d).
- **Half B** (second half) = exterior paint: `CBase*`, `CBaseEnv*`, `CDecal*`, `body*`, `pdiCgFlex*` materials, almost
  only `ReflectColorShiftBRDF` / `ScatterColorShiftBRDF` images; one slot per variation, different in 1580 bodies.
- **Half A** (first half) = second colour parts: empty on 1891 of 2035 bodies; used by soft tops (`HORO_TAG*` on the
  Mazda Roadster 1989, 0001/0012 and 0001/0034), roof and stripes (`m_roof*`, `m_stripeA..C`), cabin leather, stitching,
  seats and tags. Here the images are often the same in every slot (0012: the soft top image in all 4).
- **The last slot** of each half (k = V) is empty on all but 90 bodies; there it holds the images of the
  `..._CLR_VARI_k_PaintExtraBRDF` materials (60 only those, 30 mixed): the variant for the special paints.
- The meaning of k (which paint colour of the car uses which variation) is in the GT6 database (`specdb\GT6\DB0106.dat`,
  `database\gt6\menudb.dat`: encrypted SQLite, no reader yet). Variations are not the paint colours one by one: the
  RX-8 (0001/0001) has V = 1 but several colours (the body colour itself is a material constant from the database).

### Item 4e: every car file through the shared decoder

`gtdev mesh-check <files.tsv> <groupRegex> <out.txt>` decodes every shape of every file at every LOD level with
`Mdl3Mesh.Decode` (the decoder of the exports) and checks finite positions, indices in range, triangles at LOD0 and
positions inside the v8 shape bbox; +26 is compared per kind. Run over GT5 car hq/race/interior, wheel, tire and GT6
car body/wheel/interior, carparts, tire (`tests\mesh_check`): about 900,000 shapes, **all decoded**.
- Failures left: 3 GT5 race shapes (00800005 shapes 75 and 119: 1 vertex 1 mm outside the box; 00740016 shape 72:
  1 index of 72 out of range at the last LOD level).
- 19 files of MDL3 version 1-4 (14 GT5 interiors, 3 wheels, 2 tires, e.g. tire 0661) have another header layout and are
  not read (open).
- 2234 GT5 shape LOD levels > 0 without triangles: the part is dropped at that level (+26 is the LOD0 count).
- +26 on GT6 PMSH shapes never equals the triangles (always lower): another meaning in v14 (open). GT5 packed +26 is
  the triangles + 1 on most tire shapes (998 / 999) and higher on ~0.5% of car shapes (open, see 4c).

## Notes on PDTools (Nenkai)

What our checks agree with: header names, model/shape layout, command opcodes, FVF and array layout.
Differences and open points we found:
- The command 8 reader looks wrong (reads `flag & 0xF` twice) but it is right: all 45,522 op8 in GT6 decode exactly.
- Header 0x6C is read as an offset; in 8 GT5 files it is past the end of the file, so it is likely a size.
- Shape triangles: only triangle lists are read, strips return null ("Tristrip - FIX ME"); strips are shape flags
  nibble 6 with restart index +20 (decoded here, 4e).
- Packed meshes are read only for version >= 9 (GT6 `PMSH`). The GT5 packed reference (shape +0x2C, v8),
  used by most GT5 car body shapes, is not handled (decoded here, item 4).
- `ReadPackedElement` starts reading at `PackedFlexVertCount` (a count used as an offset): to check against the data.
- UVs: the source says itself that the scale is not understood. For GT5 plain shapes there is none (see above).
- Materials: field names agree (`MaterialDataID`, `CellGcmParamsID` = our render state index); the 0x8000 external sampler
  flag is not handled. `CellGcmParams` (40 ints) is a raw RSX method stream, now named register by register.
- SHDS header: PDTools reads the definition count at +16 and the program count at +18 like we do; the 0x2C/0x40 tables are not verified here.
- TXS3 in MDL3: the reader loads pixels only through the TXS3 texture info list, which is empty (count 0) in MDL3 files;
  TXS3Converter (Nenkai, MIT, cloned in `ref\`) uses the same reader for MDL3 input.
- Model +12 is u8 flags + u8 bounds count, not a u16 count.
- "Wing data" (0x9C) are joint constraints for all moving parts (steering, shifter, wipers), not only wings.
- The car body stream map is at header 0xDC in v14 (not 0xAC, which is 0 in every car file).
- Packed mesh keys are (shader family, material data index) pairs, not per-mesh names.
- The shader reference pass starts with two u16 counts (two lists), not one u32.
- PMSH smooth data: the raw vertex array has +22 vertices (PDTools `PackedFlexVertCount`), not +26 (`NonPackedFlexVertCount`,
  the output count); +08 and +10 are data blocks too (sizes above); mesh +08/+14 scale with +06/+07.

## Still to explain

- GT5 packed geometry: decoded (above). Open: segment +08 extra streams (geomorph?) and +0C block, H +0E, L flags,
  M Y, position format +08..+0B and +2E, the 0x40 record floats, p1/p2/p4/p5 tables (their link to p3), the shape +26 differences.
- GT6 PMSH data: decoded (above). Open: 1-byte stream, +08 entries, flag 0x01, output count > V.
- GT6 `body_s`: shape records decoded (above); `/` sub streams = colour variations (5a, above); `custom_*` groups: item 5b.
  Open: which paint colour uses which variation (encrypted GT6 database), why two halves (half A = soft top / roof /
  cabin colour parts, half B = body paint).
- Small open fields (data is fully located, meaning unknown): material block byte +04 (no match with any count),
  0x78 n, 0xB0 +28..+2E, animation row +06, VM context +0A, header 0x7C, the u16s of the pass and of the SHDS +38/+3C records,
  FP header block and VP/FP microcode contents (RSX programs).
