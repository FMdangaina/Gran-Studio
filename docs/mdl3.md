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
0xB4 holds the same values as `modelsOffset` (0x30) in v8; to check if it is a copy.

Layout order (most common, v8): materials < shaders < 0x78 < models < modelKeys < shapes < shapeKeys < fvf < textureSet < 0x54 < VM bytecode < 0x88 < textureKeys < 0xB0 < 0xB8 < 0xBC, then 0x6C last.
v14 is the same with `PMSH` (0xD0) before the models and the packed mesh keys (0xCC) at the end.

## Key tables (name -> index), verified

Record: `u32 nameOffset, u32 index`, 8 bytes, sorted by name. Names are null-terminated ASCII in a string pool near the end of the file.
The record size of 8 is exact (gap == count * 8) on every file for: shapeKeys, bones*, registerVals*, 0x5C*, wingKeys, and textureKeys in most files.
(*) same 8-byte stride; name/index meaning still to be checked.

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
| +12 / +14 | bounds count (8 = box corners) / offset of float3 array |
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

- In GT6 a shape is either plain (vertex count set, offsets 0: the data must be in `body_s`) or packed, never both.
- In GT5 most body shapes are packed (all of them in car 00010007 hq).

## Vertex formats (FVF, header 0x18 count, 0x40 offset) — verified

`Mdl3Fvf.cs`, checked with `gtdev mdl3-fvf` on all car files (GT5: 95,709 formats, GT6: 23,922).

Record 0x78 bytes: +00 name offset (`Flex`, `SimpleFill`, ...), +04 u32, +08 element list offset, +18 u8 element count,
+19 u8 vertex size, +74 array definition offset. Element 8 bytes: u32 name offset, u8 start, u8 component count,
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

## Notes on PDTools (Nenkai)

What our checks agree with: header names, model/shape layout, command opcodes, FVF and array layout.
Differences and open points we found:
- The command 8 reader looks wrong (reads `flag & 0xF` twice) but it is right: all 45,522 op8 in GT6 decode exactly.
- Header 0x6C is read as an offset; in 8 GT5 files it is past the end of the file, so it is likely a size.
- Shape triangles: only triangle lists are read, strips return null ("Tristrip - FIX ME").
- Packed meshes are read only for version >= 9 (GT6 `PMSH`). The GT5 packed reference (shape +0x2C, v8),
  used by most GT5 car body shapes, is not handled.
- `ReadPackedElement` starts reading at `PackedFlexVertCount` (a count used as an offset): to check against the data.
- UVs: the source says itself that the scale is not understood.

## Still to explain

Tables 0x54, 0x78, 0x88 (starts with a `Flex` name record), 0xA4, 0xC0 (v8), wing data (0x9C), fvf records (0x40), materials (0xE4),
VM bytecode/data (0x64..0xBC), PMSH (v14).
