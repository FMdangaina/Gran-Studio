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

## Code

No third-party code is copied into this repository. Format knowledge (field layouts, opcode operand sizes) from
PDTools is re-implemented and verified independently.

If code from these projects is ever reused, its MIT copyright notice must be copied here:

```
MIT License — Copyright (c) Nenkai
```
