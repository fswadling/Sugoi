# Tools

Asset-authoring scripts. None are needed to build or run Sugoi.

| Script | Purpose |
|---|---|
| `iqm_export_with_morphs.py` | Blender 4.1+ add-on: export an IQM (or IQE) and, when the model has shape keys, `morphs.json` + `morphs.bin`. |
| `extract_gltf_morphs.py` | Extract shape-key deltas from a `.gltf` into `morphs.json` + `morphs.bin`. |
| `morphs_to_sugoi.py` | Convert `morphs.json`/`morphs.bin` into Sugoi's `.morphs` + `.morphs.bin`, choosing up to eight morphs. |

## Install the Blender exporter

1. Use Blender 4.1 or newer.
2. **Edit → Preferences → Add-ons → Install…**, choose `tools/iqm_export_with_morphs.py`,
   and enable **Export Inter-Quake Model + Morph Sidecars**.
3. **File → Export → Inter-Quake Model + Morphs**.

The exporter writes the IQM, and (for a model with shape keys) `morphs.json` and
`morphs.bin` next to it. Exporting `.iqm` directly does not need the IQM compiler;
the upstream `iqm` kit's compiler is only for IQE/SMD/MD5/OBJ inputs.

## Convert morph sidecars for Sugoi

Sugoi reads an s-expression morph manifest plus its packed data file, with
absolute vertex ranges. The exporter's JSON addresses per-mesh ranges, so it
needs converting:

```sh
python3 tools/morphs_to_sugoi.py \
    assets/actors/Akane/morphs.json \
    assets/actors/Akane/Akane.iqm \
    --morphs Fcl_ALL_Neutral,Fcl_ALL_Joy,Fcl_ALL_Angry,Fcl_ALL_Sorrow,Fcl_ALL_Surprised,Fcl_ALL_Fun,Fcl_EYE_Close,Fcl_MTH_A \
    -o assets/actors/Akane/akane.morphs
```

- `--list-morphs` shows what the exporter produced.
- Without `--morphs`, every morph is used; this fails if there are more than
  eight, because the shader has eight weight slots.
- The order passed to `--morphs` is the order in the manifest (and therefore the
  morph indices the Inspector and cues use).
- Models without morphs need only the `.iqm`.

The manifest is referenced from the actor manifest:

```text
[[Id "akane"] [Name "Akane"] [Model "actors/Akane/Akane.iqm"] [Morphs "actors/Akane/akane.morphs"]]
```

`morphs.json`/`morphs.bin` are intermediates and are ignored under `assets/`.
The engine's output, `<name>.morphs.bin`, is also matched by the `*.bin` rule;
use `git add -f` if you version it with its model.

`extract_gltf_morphs.py` numbers meshes by flattened glTF primitive order. That
must match the IQM's mesh order; `morphs_to_sugoi.py` checks each target's
vertex count against the IQM mesh and fails loudly on a mismatch. The Blender
exporter does not have this caveat: its IQM and sidecars come from one export.

## Provenance

`iqm_export_with_morphs.py` is Lee Salzman's public-domain IQM Blender exporter,
extended with morph-sidecar output. `extract_gltf_morphs.py` and the original
versions of both scripts come from the `odin-game` project. The upstream IQM
developer kit (exporter, compiler and format description) is MIT-licensed.
`morphs_to_sugoi.py` is Sugoi-specific.
