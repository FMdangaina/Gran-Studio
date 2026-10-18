# Third-party notices and credits

## Credits

**Nenkai** — huge thanks. His open-source work on the Polyphony Digital formats is the starting point of this project:
the MDL3 header field names, the model/shape records, the model command opcodes, the vertex format (FVF) layout and
the PS3 packed mesh (PMSH) structures all come first from his research. We check every finding against all the
game files and note in `docs/` where our results agree or differ.

- **PDTools** by Nenkai, MIT license — https://github.com/Nenkai/PDTools
- **GTToolsSharp** by Nenkai, MIT license — https://github.com/Nenkai/GTToolsSharp
  (C# port of **flatz**'s gttool; used to extract the GT5/GT6 volumes. Thanks to flatz too.)

## Libraries (NuGet packages, not in the repository)

- **Vortice.Windows** (Vortice.Direct3D11, Vortice.D3DCompiler, Vortice.DXGI, Vortice.Mathematics) 3.8.3 by Amer Koleci,
  MIT license — https://github.com/amerkoleci/Vortice.Windows — Direct3D 11 bindings used by GTViewer.
- **Microsoft.Data.Sqlite** 10.0.13 by the .NET Foundation, MIT license — https://github.com/dotnet/efcore — reads the
  GT6 spec database (SQLite). It brings **SQLitePCLRaw** by Eric Sink, Apache License 2.0 —
  https://github.com/ericsink/SQLitePCL.raw — and the **SQLite** library itself (public domain, https://sqlite.org).

## Code

No third-party code is copied into this repository. Format knowledge (field layouts, opcode operand sizes) from
PDTools is re-implemented and verified independently.

`src/GTFormats/SpecDb/GtDb.cs` (GTDB spec database tables: Huffman + template-row row compression, .idi labels,
GTST string databases) is a re-implementation of Nenkai's PDTools.SpecDB (`DBT_DatabaseTable`, `Table`,
`IDI_LabelInformation`, `StringDatabase`), MIT License — Copyright (c) Nenkai. The GT5 column layouts used by
`CarCatalog` (VARIATION, CAR_NAME, GENERIC_CAR) come from PDTools' table mappings.

`src/GTFormats/SpecDb/Salsa20.cs` is a plain Salsa20 implementation (public-domain algorithm by D. J. Bernstein).
The GT6 database keys in it (spec database, menu database) and the way they are applied (zero nonce) come from
Nenkai's GTToolsSharp (`KeysetStore.cs`, `cryptsalsa`), MIT License — Copyright (c) Nenkai, and his Gran Turismo
modding guides (https://github.com/Nenkai/Gran-Turismo-Modding-Guides, "SpecDB Editing").

If code from these projects is ever reused, its MIT copyright notice must be copied here:

```
MIT License — Copyright (c) Nenkai
```
