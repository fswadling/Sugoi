#!/usr/bin/env python3
"""Extract morph target sidecar data from a .gltf file.

Output format:
- morphs.json
- morphs.bin

By default, if a sibling folder exists with the same name as the input glTF stem,
output is written there. For example:
- input: assets/Akane.gltf
- outputs: assets/Akane/morphs.json and assets/Akane/morphs.bin

Otherwise, output falls back to the input directory using:
- <stem>.morphs.json
- <stem>.morphs.bin

The JSON groups morphs by logical morph name. Each morph can contain one or more
mesh targets, which is useful when a single blendshape affects multiple mesh
primitives.

Important assumption:
The flattened primitive order in the source glTF must match the mesh order in the
runtime IQM export you plan to pair this with.

Pass `--z-up` when the IQM was converted from the same glTF by
`tools/gltf_to_iqm.py`: that tool maps glTF's Y-up axes to IQM's Z-up axes, so
the deltas must be mapped too.
"""

from __future__ import annotations

import argparse
import base64
import json
import struct
import sys
from pathlib import Path
from typing import Any

COMPONENT_TYPE_U8 = 5121
COMPONENT_TYPE_U16 = 5123
COMPONENT_TYPE_U32 = 5125
COMPONENT_TYPE_F32 = 5126

TYPE_COMPONENTS = {
    "SCALAR": 1,
    "VEC2": 2,
    "VEC3": 3,
    "VEC4": 4,
}

SPARSE_INDEX_FORMATS = {
    COMPONENT_TYPE_U8: "<B",
    COMPONENT_TYPE_U16: "<H",
    COMPONENT_TYPE_U32: "<I",
}


class MorphExtractError(RuntimeError):
    pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract morph target sidecar data from a glTF file."
    )
    parser.add_argument("input", help="Path to input .gltf file")
    parser.add_argument(
        "--json-out",
        help=(
            "Path to output morph metadata JSON "
            "(default: sibling folder morphs.json, else <input stem>.morphs.json)"
        ),
    )
    parser.add_argument(
        "--bin-out",
        help=(
            "Path to output morph delta binary "
            "(default: sibling folder morphs.bin, else <input stem>.morphs.bin)"
        ),
    )
    parser.add_argument(
        "--positions-only",
        action="store_true",
        help="Export only position deltas, ignoring normal deltas",
    )
    parser.add_argument(
        "--z-up",
        action="store_true",
        help="Map deltas from glTF's Y-up axes to IQM's Z-up axes (x, -z, y)",
    )
    return parser.parse_args()


def load_gltf_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise MorphExtractError(f"Input glTF not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise MorphExtractError(f"Failed to parse glTF JSON: {path}: {exc}") from exc


def load_buffers(doc: dict[str, Any], gltf_path: Path) -> list[bytes]:
    base_dir = gltf_path.parent
    buffers: list[bytes] = []

    for i, buffer_desc in enumerate(doc.get("buffers", [])):
        uri = buffer_desc.get("uri")
        if uri is None:
            raise MorphExtractError(
                f"Buffer {i} has no uri. .glb is not supported by this extractor."
            )

        if uri.startswith("data:"):
            comma_index = uri.find(",")
            if comma_index < 0 or ";base64" not in uri[:comma_index]:
                raise MorphExtractError(f"Unsupported data URI for buffer {i}")
            try:
                data = base64.b64decode(uri[comma_index + 1 :])
            except Exception as exc:  # pragma: no cover - defensive
                raise MorphExtractError(f"Invalid base64 data for buffer {i}") from exc
        else:
            buffer_path = base_dir / uri
            try:
                data = buffer_path.read_bytes()
            except FileNotFoundError as exc:
                raise MorphExtractError(
                    f"Buffer file not found: {buffer_path}"
                ) from exc

        expected_size = int(buffer_desc.get("byteLength", len(data)))
        if len(data) < expected_size:
            raise MorphExtractError(
                f"Buffer {i} shorter than declared byteLength: {len(data)} < {expected_size}"
            )

        buffers.append(data)

    return buffers


def get_buffer_view_bytes(
    doc: dict[str, Any], buffers: list[bytes], buffer_view_index: int
) -> bytes:
    buffer_view = doc["bufferViews"][buffer_view_index]
    buffer_data = buffers[buffer_view["buffer"]]
    offset = int(buffer_view.get("byteOffset", 0))
    size = int(buffer_view["byteLength"])
    return buffer_data[offset : offset + size]


def read_dense_accessor_vec3_f32(
    doc: dict[str, Any], buffers: list[bytes], accessor: dict[str, Any]
) -> list[float]:
    count = int(accessor["count"])
    result = [0.0] * (count * 3)

    if "bufferView" not in accessor:
        return result

    buffer_view_index = accessor["bufferView"]
    buffer_view = doc["bufferViews"][buffer_view_index]
    buffer_data = buffers[buffer_view["buffer"]]

    view_offset = int(buffer_view.get("byteOffset", 0))
    accessor_offset = int(accessor.get("byteOffset", 0))
    stride = int(buffer_view.get("byteStride", 12))
    start = view_offset + accessor_offset

    for i in range(count):
        value_offset = start + i * stride
        x, y, z = struct.unpack_from("<3f", buffer_data, value_offset)
        base = i * 3
        result[base + 0] = x
        result[base + 1] = y
        result[base + 2] = z

    return result


def apply_sparse_vec3_f32(
    result: list[float],
    doc: dict[str, Any],
    buffers: list[bytes],
    accessor: dict[str, Any],
) -> None:
    sparse = accessor.get("sparse")
    if not sparse:
        return

    sparse_count = int(sparse["count"])
    indices_desc = sparse["indices"]
    values_desc = sparse["values"]

    index_component_type = int(indices_desc["componentType"])
    if index_component_type not in SPARSE_INDEX_FORMATS:
        raise MorphExtractError(
            f"Unsupported sparse index component type: {index_component_type}"
        )

    index_fmt = SPARSE_INDEX_FORMATS[index_component_type]
    index_size = struct.calcsize(index_fmt)

    indices_bytes = get_buffer_view_bytes(doc, buffers, int(indices_desc["bufferView"]))
    values_bytes = get_buffer_view_bytes(doc, buffers, int(values_desc["bufferView"]))

    indices_offset = int(indices_desc.get("byteOffset", 0))
    values_offset = int(values_desc.get("byteOffset", 0))

    for sparse_i in range(sparse_count):
        index = struct.unpack_from(
            index_fmt, indices_bytes, indices_offset + sparse_i * index_size
        )[0]
        x, y, z = struct.unpack_from("<3f", values_bytes, values_offset + sparse_i * 12)
        base = index * 3
        result[base + 0] = x
        result[base + 1] = y
        result[base + 2] = z


def read_accessor_vec3_f32(
    doc: dict[str, Any], buffers: list[bytes], accessor_index: int
) -> list[float]:
    accessor = doc["accessors"][accessor_index]

    if accessor.get("type") != "VEC3":
        raise MorphExtractError(
            f"Accessor {accessor_index} is not VEC3 (got {accessor.get('type')})"
        )
    if int(accessor.get("componentType", -1)) != COMPONENT_TYPE_F32:
        raise MorphExtractError(
            f"Accessor {accessor_index} is not FLOAT VEC3 (componentType={accessor.get('componentType')})"
        )

    result = read_dense_accessor_vec3_f32(doc, buffers, accessor)
    apply_sparse_vec3_f32(result, doc, buffers, accessor)
    return result


def get_target_names(mesh: dict[str, Any], target_count: int) -> list[str]:
    extras = mesh.get("extras") or {}
    names = mesh.get("targetNames") or extras.get("targetNames") or []
    if not isinstance(names, list):
        names = []

    result: list[str] = []
    for i in range(target_count):
        name = names[i] if i < len(names) else f"Target_{i}"
        if not isinstance(name, str) or not name:
            name = f"Target_{i}"
        result.append(name)
    return result


def to_z_up(values: list[float]) -> list[float]:
    """Map a flat vec3 list from glTF's Y-up axes to IQM/Blender's Z-up axes."""
    result = [0.0] * len(values)
    for i in range(0, len(values), 3):
        x, y, z = values[i], values[i + 1], values[i + 2]
        result[i] = x
        result[i + 1] = -z
        result[i + 2] = y
    return result


def collect_morphs(
    doc: dict[str, Any],
    buffers: list[bytes],
    positions_only: bool,
    z_up: bool,
) -> tuple[list[dict[str, Any]], bytes]:
    morphs_by_name: dict[str, dict[str, Any]] = {}
    packed_f32: list[float] = []
    flattened_mesh_index = 0

    meshes = doc.get("meshes", [])
    for mesh in meshes:
        primitives = mesh.get("primitives", [])
        mesh_name = mesh.get("name", f"Mesh_{flattened_mesh_index}")

        for primitive_index, primitive in enumerate(primitives):
            targets = primitive.get("targets")
            if not targets:
                flattened_mesh_index += 1
                continue

            position_accessor_index = primitive.get("attributes", {}).get("POSITION")
            if position_accessor_index is None:
                raise MorphExtractError(
                    f"Primitive {mesh_name}[{primitive_index}] has morphs but no POSITION attribute"
                )

            base_positions = read_accessor_vec3_f32(
                doc, buffers, int(position_accessor_index)
            )
            vertex_count = len(base_positions) // 3
            target_names = get_target_names(mesh, len(targets))

            for target_index, target in enumerate(targets):
                name = target_names[target_index]
                position_accessor = target.get("POSITION")
                if position_accessor is None:
                    raise MorphExtractError(
                        f"Morph target '{name}' on {mesh_name}[{primitive_index}] has no POSITION accessor"
                    )

                position_deltas = read_accessor_vec3_f32(
                    doc, buffers, int(position_accessor)
                )
                if len(position_deltas) != len(base_positions):
                    raise MorphExtractError(
                        f"Morph target '{name}' vertex count mismatch on {mesh_name}[{primitive_index}]"
                    )
                if z_up:
                    position_deltas = to_z_up(position_deltas)

                position_offset_f32 = len(packed_f32)
                packed_f32.extend(position_deltas)

                has_normal_deltas = False
                normal_offset_f32 = 0
                normal_accessor = target.get("NORMAL")
                if normal_accessor is not None and not positions_only:
                    normal_deltas = read_accessor_vec3_f32(
                        doc, buffers, int(normal_accessor)
                    )
                    if len(normal_deltas) != len(base_positions):
                        raise MorphExtractError(
                            f"Morph target '{name}' normal count mismatch on {mesh_name}[{primitive_index}]"
                        )
                    if z_up:
                        normal_deltas = to_z_up(normal_deltas)
                    has_normal_deltas = True
                    normal_offset_f32 = len(packed_f32)
                    packed_f32.extend(normal_deltas)

                morph = morphs_by_name.setdefault(name, {"name": name, "targets": []})
                morph["targets"].append(
                    {
                        "mesh_index": flattened_mesh_index,
                        "source_mesh_name": mesh_name,
                        "source_primitive_index": primitive_index,
                        "vertex_count": vertex_count,
                        "has_normal_deltas": has_normal_deltas,
                        "position_offset_f32": position_offset_f32,
                        "normal_offset_f32": normal_offset_f32,
                    }
                )

            flattened_mesh_index += 1

    binary = struct.pack(f"<{len(packed_f32)}f", *packed_f32) if packed_f32 else b""
    ordered_morphs = sorted(morphs_by_name.values(), key=lambda morph: morph["name"])
    return ordered_morphs, binary


def write_outputs(
    json_path: Path,
    bin_path: Path,
    gltf_path: Path,
    morphs: list[dict[str, Any]],
    binary: bytes,
) -> None:
    metadata = {
        "version": 1,
        "source": gltf_path.name,
        "binary": bin_path.name,
        "morphs": morphs,
    }

    json_path.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    bin_path.write_bytes(binary)


def default_output_paths(gltf_path: Path) -> tuple[Path, Path]:
    default_stem = gltf_path.with_suffix("")
    sibling_folder = gltf_path.parent / default_stem.name
    if sibling_folder.is_dir():
        return sibling_folder / "morphs.json", sibling_folder / "morphs.bin"
    return default_stem.with_suffix(".morphs.json"), default_stem.with_suffix(
        ".morphs.bin"
    )


def main() -> int:
    args = parse_args()
    gltf_path = Path(args.input)
    if gltf_path.suffix.lower() != ".gltf":
        raise MorphExtractError("This extractor currently supports .gltf files only")

    default_json_path, default_bin_path = default_output_paths(gltf_path)
    json_path = Path(args.json_out) if args.json_out else default_json_path
    bin_path = Path(args.bin_out) if args.bin_out else default_bin_path

    doc = load_gltf_json(gltf_path)
    buffers = load_buffers(doc, gltf_path)
    morphs, binary = collect_morphs(
        doc, buffers, positions_only=args.positions_only, z_up=args.z_up
    )
    write_outputs(json_path, bin_path, gltf_path, morphs, binary)

    target_count = sum(len(morph["targets"]) for morph in morphs)
    print(f"wrote {json_path}")
    print(f"wrote {bin_path} ({len(binary)} bytes)")
    print(f"morphs: {len(morphs)} logical morph(s), {target_count} mesh target(s)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except MorphExtractError as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1)
