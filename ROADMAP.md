# Roadmap

One **Now** item at a time; it is closed before the next one starts.
Input data: extracted volumes in `F:\GT_DEV\GT5_VOL` and `F:\GT_DEV\GT6_VOL` (read only).
Tests and dumps: `tests\<topic>` (not committed).

## Now

### 2. MDL3 inventory
Read the header and top-level tables of every MDL3 in GT5 (`car\hq`, `car\race`,
`car\interior`, `wheel`) and GT6 (`car\...\body`, `wheel`, `interior`).
Done when every table offset/count is explained and checked on all files
(no file left with unknown bytes in the header). Findings in `docs/mdl3.md`.

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
- [ ] 2f. remaining tables: 0x54, 0x78, 0x88, 0xA4, 0xC0, wing data, VM blocks, PMSH (v14)

## Next

3. **TXS3 textures** — decode DXT/RGBA formats, visual check. Find where GT6 car image pixels are (offset 0 in `body`).
   Reference: Nenkai's TXS3Converter (MIT, cloned in `ref\TXS3Converter`, uses PDTools' TextureSet3 reader).
4. **Geometry** — unpacked meshes first, then packed vertices, then Edge-compressed indices.
   Done only when it works on every car.
5. **GT6 `body_s`** — understand the compressed companion of `body`, then GT6 vs GT5 differences.
6. **Viewer** — Direct3D 11 (Vortice.Windows), read only.

## Later / open questions

- Compare results with Nenkai's PDTools model readers (MIT), note differences in `docs/`.
- Earlier research in `Geom_research\` (car 00010007) is unverified: re-check it, do not build on it.

## Done

- **1. Project setup** (2026-10-17) — repo, solution (`GTFormats`, `GTViewer`, `gtdev`), license, notices.
