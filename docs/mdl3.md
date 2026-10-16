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
| 0x6C | "vmInstanceOffset" | always the highest value; in 8 v8 files it is past the end of the file: it is probably a size, not an offset |

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

## Records with sub-data (stride known, size of the trailing data not yet)

- **models** (0x10, 0x30): 0x30-byte records; start with a bounding sphere (x, y, z, radius floats), then a count and two offsets into data after the table.
- **shapes** (0x14, 0x38): 0x30-byte records. This is the "mesh table" of the earlier research.

## Still to explain

Tables 0x54, 0x78, 0x88 (starts with a `Flex` name record), 0xA4, 0xC0 (v8), wing data (0x9C), fvf records (0x40), materials (0xE4),
VM bytecode/data (0x64..0xBC), PMSH (v14).
