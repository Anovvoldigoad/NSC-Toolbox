# NUCC/XFBIN structure observations — Switch 1.70

Source: 15 extracted user-supplied Switch XFBIN files (effects, skill,
and miscellaneous resources). These observations do not verify decoding
of geometry, skeletal animations, or moveset parameter payloads.

| Header offset | Metadata | Observed count convention |
| --- | --- | --- |
| 0x1C | NUCC type strings | Raw count, no extra sentinel |
| 0x20 | NUCC type string-table size | Bytes, big-endian |
| 0x24 | Resource paths | Raw count includes a sentinel |
| 0x28 | Resource path-table size | Bytes, big-endian |
| 0x2C | Resource names | Raw count includes a sentinel |
| 0x30 | Resource name-table size | Bytes, big-endian |
| 0x38 | First binary table bytes | Bytes, big-endian |
| 0x3C | Second binary-table size | Number of 4-byte words |

For every one of these 15 samples, actual nonempty string counts were:
NUCC = raw count; Paths = raw count - 1; Names = raw count - 1.

Observed type identifiers included `nuccChunkModel`,
`nuccChunkClump`, `nuccChunkAnm`, `nuccChunkCoord`,
`nuccChunkTexture`, and `nuccChunkBinary`. Types indicate content
categories, not a decoded renderable model, skeleton or attack mapping.

Paths in XFBIN sometimes retain development-platform path separators and
drive prefixes; treat these strings as metadata, never local filesystem
paths to write. No original game data is included in this repository.

Next validation needed: match chunk indices to paths/names, inspect actual
mesh vertex/index layouts, texture and material links, bone transforms,
skeletal clip timing, and then map these to GLB or native Android rendering.
Do not infer PC binary offsets transfer directly to Switch.
