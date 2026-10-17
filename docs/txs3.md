# TXS3 textures (inside MDL3)

Verified on 23,224 car / interior / wheel / tire / carparts MDL3 files (GT5 v4-v8, GT6 v13-v14) with
`gtdev txs3-survey` (layout + size rule) and `gtdev txs3-png` (decode every image). The 17 old v1/v2/v4 files
outside the 2f coverage are not handled. Reference: Nenkai's PDTools `TextureSet3` / `PGLUCellTextureInfo` (MIT).

## Image record (0x20, TXS3 +1C, count at +16)

| Off | Type | Meaning |
|---|---|---|
| +00 | u32 | pixel offset, absolute in the MDL3 (GT6 car `body`: 0 = pixels not in the file, see below) |
| +04 | u32 | size in bytes (rule below) |
| +08 | u8 | 1 = cube map (== texture info FORMAT bit 2, all files) |
| +09 | u8 | format, CELL_GCM_TEXTURE_* (0x20 = linear LN, otherwise swizzled) |
| +0A | u8 | mipmap count (== FORMAT bits 16-23) |
| +0B | u8 | location 1 local / 2 main: equals the FORMAT location of at least one texture info using the image, except 7 GT6 hq BRDF 3D images (open) |
| +0C/+0E | u16 | width / height |
| +10 | u16 | depth: > 1 exactly for 3D textures (FORMAT dimension 3), == CONTROL3 bits 20-31 |
| +12..+1F | | zero |

Several texture infos can share one image (GT6 body: about 10% of images), with different locations.

## Formats found

Only five: DXT1 (0x86), DXT23 (0x87), DXT45 (0x88), A8R8G8B8 swizzled (0x85) and A8R8G8B8 linear (0xA5).
Non-power-of-2 sizes are always linear. Cubes are DXT45 only. 3D textures: wheels (A8R8G8B8 32x16x8, DXT1 64x64x8,
DXT45 32x16x8, GT5 and GT6 race wheels) and GT6 bodies (A8R8G8B8 64x32x4 BRDF, linear 4x1x4).

Channel remap (texture info +14, CONTROL1): 0xAAE4 (identity) on every DXT, 0xAA1B (reversed) on every A8R8G8B8:
the bytes in memory are B, G, R, A.

## Size rule (0 failures)

- Level size: DXT = ceil(w/4) x ceil(h/4) x 8 (DXT1) or 16; others w x h x bpp; linear = pitch x h.
- Mip levels halve down to 1 (not stopped at 4x4 for DXT). 3D slices are contiguous inside a level.
- A face (all mips of all slices) is padded to **128 bytes**; a cube has 6 faces one after the other.
- Linear images keep the texture info pitch (CONTROL3 bits 0-19) on **every** mip level.
  Mipmapped non-power-of-2 linear images are *allocated* as if the pitch were that of the next power-of-2 width
  (171x64 x 8 mips: pixels use pitch 684 = 87,552 bytes, record size 131,072 = pitch 1024), the rest is zero.

## Decoding

`Txs3Image.DecodeLevel0` (GTFormats/Txs3): DXT blocks as on PC (little-endian, row order, never swizzled);
swizzled A8R8G8B8 = Morton order, bits of x, y (and z for 3D) interleaved x first while each dimension still has bits;
linear rows at the texture info pitch.
Visual checks (tests\txs3): decals, calipers, wheel faces, linear 171x64 caliper, cube: correct.
3D DXT1 wheel texture: plain block order per slice is the only coherent one of three tried (row / 2D Morton / 3D Morton),
to confirm in the viewer. Images look upside down (decal text): v = 0 at the bottom, to confirm with the UVs (item 4).

## GT6 car bodies

87,258 of 88,287 images in `car\hq\body` and 81,156 of 81,693 in `race\body` have offset 0: the pixels are not in
`body` (sizes are known and follow the rule above). GT6 interior / wheel / tire / carparts images are all in the file.

**Where they are (3d): in `body_s`**, found through the stream map at `body` header 0xDC (layout in `docs/mdl3.md`,
"Shape stream map"). Checked with `gtdev body-s` on all 2454 GT6 car bodies (`tests\txs3_body\check_all.txt`), 0 failures:
- every one of the 87,258 + 81,156 offset-0 images is listed in at least one `body_s` bundle, with room for its full
  record size, and decodes (DXT1/23/45, A8R8G8B8 swizzled and linear); no image that has pixels in `body` is in `body_s`.
- pixel offsets are relative to the start of the inflated bundle, 0x80-aligned; small images sit in the bundle header part,
  large ones in the data part (hq: 26,134 header / 66,717 data). Images, tables and the bundle header never overlap.
- An image can be in several bundles (paint-colour sub streams of `/`, the variants of `custom_*` parts):
  the 10,886 extra copies all have **the same pixels** as the first one, so each bundle is self-contained.
- Visual check (tests\txs3_body\png_*): RX-8 (0001/0001 hq) body atlas, decals, shadow, caliper, disc: correct.

`gtdev txs3-png <body> <outDir>` now takes the pixels from `body_s` (every copy, suffix `_vN` when there are several).
