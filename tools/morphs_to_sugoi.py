#!/usr/bin/env python3
"""Convert odin-style morph sidecars to Sugoi's morph sidecar format.

`iqm_export_with_morphs.py` (and `extract_gltf_morphs.py`) emit:

    morphs.json   metadata, with targets addressed by IQM mesh index
    morphs.bin    little-endian float32 position/normal deltas

Sugoi reads a Prime s-expression manifest plus a packed float32 data file:

    <name>.morphs       [Version 1] [Data "<name>.morphs.bin"] [Morphs ...]
    <name>.morphs.bin   little-endian float32 deltas

The difference is the addressing. Sugoi targets the actor's combined vertex
buffer with absolute `FirstVertex` ranges and sequential data offsets; the
exporter targets per-mesh ranges and leaves the source offsets sparse. This
script maps each target through its IQM mesh's `first_vertex`, repacks every
target's deltas sequentially, and writes the manifest.

Morph order is meaningful: a morph's index is its position in the manifest and
the engine caps an asset at 8 morphs. Pass `--morphs` to choose and order the
morphs you want (the JSON is alphabetically sorted, and may contain more than
the engine can hold).

Examples:

    # See what the exporter produced
    python3 tools/morphs_to_sugoi.py assets/actors/Akane/morphs.json \
        assets/actors/Akane/Akane.iqm --list-morphs

    # Convert eight chosen morphs, in the order they should appear
    python3 tools/morphs_to_sugoi.py assets/actors/Akane/morphs.json \
        assets/actors/Akane/Akane.iqm \
        --morphs Fcl_ALL_Neutral,Fcl_ALL_Joy,Fcl_ALL_Angry,Fcl_ALL_Sorrow,Fcl_ALL_Surprised,Fcl_ALL_Fun,Fcl_EYE_Close,Fcl_MTH_A \
        -o assets/actors/Akane/akane.morphs
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from pathlib import Path
from typing import Any

# Mirrors Gpu.MorphsMax in render/Gpu.fs; a morph index is a shader weight slot.
MAX_MORPHS = 8

IQM_MAGIC = b"INTERQUAKEMODEL\x00"
IQM_VERSION = 2
IQM_HEADER_MESHES = 36          # num_meshes
IQM_HEADER_MESH_OFFSET = 40     # ofs_meshes
IQM_HEADER_VERTEXES = 48        # num_vertexes
IQM_MESH_STRUCT = struct.Struct("<6I")
IQM_MESH_FIRST_VERTEX = 2       # field index within the mesh struct
IQM_MESH_NUM_VERTEXES = 3


class ConversionError(RuntimeError):
    pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert morphs.json/morphs.bin to Sugoi's .morphs sidecars."
    )
    parser.add_argument("morphs_json", help="Path to the exporter's morphs.json")
    parser.add_argument("iqm", help="The IQM exported with that morphs.json")
    parser.add_argument(
        "-o",
        "--out",
        help="Output manifest (default: <iqm stem>.morphs beside the IQM)",
    )
    parser.add_argument(
        "--bin-out",
        help="Output delta file (default: <manifest>.bin)",
    )
    parser.add_argument(
        "--morphs",
        help=(
            "Comma-separated morph names to include, in manifest order. "
            "Required when the JSON holds more than 8."
        ),
    )
    parser.add_argument(
        "--list-morphs",
        action="store_true",
        help="List the morph names in the JSON and exit",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing output files",
    )
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ConversionError(f"morphs JSON not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ConversionError(f"failed to parse morphs JSON: {path}: {exc}") from exc


def read_iqm_meshes(path: Path) -> tuple[int, list[tuple[int, int]]]:
    """Return (num_vertexes, [(first_vertex, num_vertexes), ...]) from an IQM."""
    try:
        data = path.read_bytes()
    except FileNotFoundError as exc:
        raise ConversionError(f"IQM not found: {path}") from exc

    if len(data) < 124:
        raise ConversionError(f"IQM is too short: {path}")
    if data[:16] != IQM_MAGIC:
        raise ConversionError(f"not an IQM file: {path}")
    version = struct.unpack_from("<I", data, 16)[0]
    if version != IQM_VERSION:
        raise ConversionError(f"unsupported IQM version {version} (expected {IQM_VERSION})")

    num_meshes = struct.unpack_from("<I", data, IQM_HEADER_MESHES)[0]
    ofs_meshes = struct.unpack_from("<I", data, IQM_HEADER_MESH_OFFSET)[0]
    num_vertexes = struct.unpack_from("<I", data, IQM_HEADER_VERTEXES)[0]

    meshes: list[tuple[int, int]] = []
    for i in range(num_meshes):
        offset = ofs_meshes + i * IQM_MESH_STRUCT.size
        if offset + IQM_MESH_STRUCT.size > len(data):
            raise ConversionError(f"IQM mesh table runs past the file at mesh {i}")
        fields = IQM_MESH_STRUCT.unpack_from(data, offset)
        meshes.append(
            (fields[IQM_MESH_FIRST_VERTEX], fields[IQM_MESH_NUM_VERTEXES])
        )
    return num_vertexes, meshes


def choose_morphs(available: list[str], requested: str | None) -> list[str]:
    if requested is None:
        chosen = available
    else:
        chosen = [name.strip() for name in requested.split(",") if name.strip()]
        if not chosen:
            raise ConversionError("--morphs named no morphs")
        missing = [name for name in chosen if name not in available]
        if missing:
            raise ConversionError(
                "unknown morph(s): %s\navailable:\n  %s"
                % (", ".join(missing), "\n  ".join(available))
            )
        if len(set(chosen)) != len(chosen):
            raise ConversionError("--morphs names a morph more than once")

    if len(chosen) == 0:
        raise ConversionError("the morphs JSON contains no morphs")
    if len(chosen) > MAX_MORPHS:
        raise ConversionError(
            "%d morphs selected but Sugoi supports at most %d; "
            "choose up to %d with --morphs (see --list-morphs)"
            % (len(chosen), MAX_MORPHS, MAX_MORPHS)
        )
    return chosen


def read_deltas(
    data: bytes, source_offset: int, vertex_count: int, label: str
) -> list[float]:
    count = vertex_count * 3
    byte_offset = source_offset * 4
    if source_offset < 0 or byte_offset + count * 4 > len(data):
        raise ConversionError(
            "%s offset %d + %d floats runs past the %d-float data file"
            % (label, source_offset, count, len(data) // 4)
        )
    return list(struct.unpack_from("<%df" % count, data, byte_offset))


def convert(
    doc: dict[str, Any],
    data: bytes,
    meshes: list[tuple[int, int]],
    num_vertexes: int,
    chosen: list[str],
) -> tuple[list[float], str, int, int]:
    """Return (packed floats, manifest body, morph count, target count)."""
    if doc.get("version") != 1:
        raise ConversionError(
            "unsupported morphs JSON version %r (expected 1)" % doc.get("version")
        )

    by_name = {morph["name"]: morph for morph in doc.get("morphs", [])}
    packed: list[float] = []
    morph_records: list[str] = []
    target_count = 0

    for name in chosen:
        morph = by_name[name]
        target_records: list[str] = []
        for target in morph.get("targets", []):
            mesh_index = target["mesh_index"]
            if mesh_index < 0 or mesh_index >= len(meshes):
                raise ConversionError(
                    "morph '%s': mesh_index %d is outside the IQM's %d meshes"
                    % (name, mesh_index, len(meshes))
                )

            first_vertex, mesh_vertex_count = meshes[mesh_index]
            vertex_count = target["vertex_count"]
            if vertex_count != mesh_vertex_count:
                raise ConversionError(
                    "morph '%s': target has %d vertices but IQM mesh %d has %d"
                    % (name, vertex_count, mesh_index, mesh_vertex_count)
                )
            if first_vertex + vertex_count > num_vertexes:
                raise ConversionError(
                    "morph '%s': mesh %d range [%d, %d) is outside the IQM's %d vertices"
                    % (
                        name,
                        mesh_index,
                        first_vertex,
                        first_vertex + vertex_count,
                        num_vertexes,
                    )
                )

            position_offset = len(packed)
            packed.extend(
                read_deltas(
                    data,
                    target["position_offset_f32"],
                    vertex_count,
                    "morph '%s' position" % name,
                )
            )

            normal_offset = 0
            if target["has_normal_deltas"]:
                normal_offset = len(packed)
                packed.extend(
                    read_deltas(
                        data,
                        target["normal_offset_f32"],
                        vertex_count,
                        "morph '%s' normal" % name,
                    )
                )

            target_records.append(
                "[[FirstVertex %d] [VertexCount %d] [HasNormalDeltas %s] "
                "[PositionOffset %d] [NormalOffset %d]]"
                % (
                    first_vertex,
                    vertex_count,
                    "true" if target["has_normal_deltas"] else "false",
                    position_offset,
                    normal_offset,
                )
            )
            target_count += 1

        if not target_records:
            raise ConversionError("morph '%s' has no targets" % name)

        morph_records.append(
            "[[Name %s] [Targets [%s]]]"
            % (json.dumps(name), " ".join(target_records))
        )

    return packed, " ".join(morph_records), len(chosen), target_count


def write_outputs(
    manifest_path: Path,
    bin_path: Path,
    packed: list[float],
    morphs_body: str,
    force: bool,
) -> None:
    for output in (manifest_path, bin_path):
        if output.exists() and not force:
            raise ConversionError(
                "%s already exists; pass --force to overwrite" % output
            )

    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    bin_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        data_name = os.path.relpath(bin_path, manifest_path.parent)
    except ValueError:
        data_name = bin_path.name
    data_name = data_name.replace(os.sep, "/")

    manifest = '[[Version 1] [Data %s] [Morphs [%s]]]\n' % (
        json.dumps(data_name),
        morphs_body,
    )
    manifest_path.write_text(manifest, encoding="utf-8")

    with bin_path.open("wb") as output:
        chunk_size = 65536
        for start in range(0, len(packed), chunk_size):
            chunk = packed[start : start + chunk_size]
            output.write(struct.pack("<%df" % len(chunk), *chunk))


def main() -> int:
    args = parse_args()
    json_path = Path(args.morphs_json)
    iqm_path = Path(args.iqm)

    try:
        doc = load_json(json_path)
        available = [morph["name"] for morph in doc.get("morphs", [])]

        if args.list_morphs:
            for name in available:
                print(name)
            return 0

        chosen = choose_morphs(available, args.morphs)

        manifest_path = (
            Path(args.out)
            if args.out
            else iqm_path.with_suffix(".morphs")
        )
        bin_path = (
            Path(args.bin_out)
            if args.bin_out
            else Path(str(manifest_path) + ".bin")
        )

        source_name = doc.get("binary", "morphs.bin")
        source_bin_path = json_path.parent / source_name
        try:
            data = source_bin_path.read_bytes()
        except FileNotFoundError as exc:
            raise ConversionError(f"morph data not found: {source_bin_path}") from exc
        if len(data) % 4 != 0:
            raise ConversionError(
                "%s is not a whole number of float32 values" % source_bin_path
            )

        num_vertexes, meshes = read_iqm_meshes(iqm_path)
        packed, morphs_body, morph_count, target_count = convert(
            doc, data, meshes, num_vertexes, chosen
        )
        write_outputs(manifest_path, bin_path, packed, morphs_body, args.force)
    except ConversionError as exc:
        print("morphs_to_sugoi: %s" % exc, file=sys.stderr)
        return 1

    print(
        "wrote %s and %s (%d morph(s), %d target(s), %d floats)"
        % (manifest_path, bin_path, morph_count, target_count, len(packed))
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
