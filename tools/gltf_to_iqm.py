#!/usr/bin/env python3
"""Convert a glTF 2.0 skinned model to IQM v2 for Sugoi, without Blender.

Blender is the usual authoring path (`tools/iqm_export_with_morphs.py`); this
script converts a model that already exists as glTF, and produces the same
shape of IQM the engine expects:

- one IQM mesh per glTF primitive, named after the mesh node (`Body`, `Face`,
  `Hair`), its material string the base-colour texture's file name;
- the skin's joints, in parent-before-child order, with rest poses and every
  animation resampled at the source frame rate;
- glTF's Y-up axes converted to IQM's Z-up (Blender) axes, so `ActorRender`'s
  `upAxisFix` maps the model exactly as it maps a Blender export;
- float positions/uvs/normals and ubyte blend indices/weights.

Morph targets are not part of an IQM. Extract them from the same glTF with
`--z-up`, then convert them against the IQM:

    python3 tools/extract_gltf_morphs.py Shinji.gltf --z-up
    python3 tools/morphs_to_sugoi.py Shinji.morphs.json Shinji.iqm --morphs ... -o shinji.morphs

Usage:

    python3 tools/gltf_to_iqm.py assets/actors/Shinji/Shinji.gltf -o assets/actors/Shinji/Shinji.iqm
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import struct
import sys
from bisect import bisect_right
from pathlib import Path
from typing import Any

# ── glTF constants ──

COMPONENTS = {
    5120: ("b", 1, 127.0),
    5121: ("B", 1, 255.0),
    5122: ("h", 2, 32767.0),
    5123: ("H", 2, 65535.0),
    5125: ("I", 4, None),
    5126: ("f", 4, None),
}
TYPE_COUNTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

# ── IQM constants ──

IQM_MAGIC = b"INTERQUAKEMODEL\x00"
IQM_VERSION = 2
IQM_HEADER_SIZE = 124

VERTEX_POSITION = 0
VERTEX_TEXCOORD = 1
VERTEX_NORMAL = 2
VERTEX_BLEND_INDICES = 4
VERTEX_BLEND_WEIGHTS = 5
VERTEX_FORMAT_UBYTE = 1
VERTEX_FORMAT_FLOAT = 7

# Every joint writes all ten channels. The pose table is shared by all clips,
# so a constant channel packed as offset-with-zero-scale is always exact.
CHANNEL_MASK = 0x3FF
CHANNEL_COUNT = 10

# glTF (Y-up) -> IQM/Blender (Z-up): (x, y, z) -> (x, -z, y).
AXIS_QUAT = (math.sqrt(0.5), 0.0, 0.0, math.sqrt(0.5))


class ConversionError(RuntimeError):
    pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert a glTF 2.0 skinned model to IQM v2."
    )
    parser.add_argument("input", help="Path to input .gltf file")
    parser.add_argument(
        "-o",
        "--out",
        help="Output IQM path (default: the input with a .iqm suffix)",
    )
    parser.add_argument(
        "--no-animations",
        action="store_true",
        help="Drop the glTF animations (geometry and joints only)",
    )
    return parser.parse_args()


# ── glTF reading ──


def load_document(path: Path) -> tuple[dict[str, Any], list[bytes]]:
    if path.suffix.lower() != ".gltf":
        raise ConversionError("this converter supports .gltf files only (not .glb)")
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ConversionError(f"glTF not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ConversionError(f"failed to parse {path}: {exc}") from exc

    buffers: list[bytes] = []
    for index, desc in enumerate(doc.get("buffers", [])):
        uri = desc.get("uri")
        if uri is None:
            raise ConversionError(f"buffer {index} has no uri (.glb is unsupported)")
        if uri.startswith("data:"):
            comma = uri.find(",")
            if comma < 0 or ";base64" not in uri[:comma]:
                raise ConversionError(f"unsupported data URI for buffer {index}")
            data = base64.b64decode(uri[comma + 1 :])
        else:
            buffer_path = path.parent / uri
            try:
                data = buffer_path.read_bytes()
            except FileNotFoundError as exc:
                raise ConversionError(f"buffer file not found: {buffer_path}") from exc
        expected = int(desc.get("byteLength", len(data)))
        if len(data) < expected:
            raise ConversionError(
                f"buffer {index} is shorter than declared: {len(data)} < {expected}"
            )
        buffers.append(data)
    return doc, buffers


def accessor_values(
    doc: dict[str, Any], buffers: list[bytes], index: int
) -> tuple[list[float], int]:
    """Return (flat values, components-per-element) for an accessor."""
    accessor = doc["accessors"][index]
    count = int(accessor["count"])
    kind = accessor["type"]
    if kind not in TYPE_COUNTS:
        raise ConversionError(f"accessor {index}: unsupported type {kind}")
    components = TYPE_COUNTS[kind]
    fmt, size, normalizer = COMPONENTS[int(accessor["componentType"])]
    values = [0.0] * (count * components)

    buffer_view_index = accessor.get("bufferView")
    if buffer_view_index is not None:
        view = doc["bufferViews"][buffer_view_index]
        data = buffers[view["buffer"]]
        start = int(view.get("byteOffset", 0)) + int(accessor.get("byteOffset", 0))
        stride = int(view.get("byteStride", size * components))
        for i in range(count):
            unpacked = struct.unpack_from("<" + fmt * components, data, start + i * stride)
            values[i * components : (i + 1) * components] = unpacked

    sparse = accessor.get("sparse")
    if sparse is not None:
        indices_desc = sparse["indices"]
        values_desc = sparse["values"]
        index_fmt, index_size, _ = COMPONENTS[int(indices_desc["componentType"])]
        value_fmt, value_size, _ = COMPONENTS[int(accessor["componentType"])]
        indices_view = doc["bufferViews"][indices_desc["bufferView"]]
        values_view = doc["bufferViews"][values_desc["bufferView"]]
        indices_data = buffers[indices_view["buffer"]]
        values_data = buffers[values_view["buffer"]]
        indices_start = int(indices_view.get("byteOffset", 0)) + int(
            indices_desc.get("byteOffset", 0)
        )
        values_start = int(values_view.get("byteOffset", 0)) + int(
            values_desc.get("byteOffset", 0)
        )
        for k in range(int(sparse["count"])):
            target = struct.unpack_from(
                "<" + index_fmt, indices_data, indices_start + k * index_size
            )[0]
            unpacked = struct.unpack_from(
                "<" + value_fmt * components, values_data, values_start + k * value_size * components
            )
            values[target * components : (target + 1) * components] = unpacked

    if accessor.get("normalized") and normalizer is not None:
        values = [max(-1.0, v / normalizer) for v in values]

    return values, components


def read_vec(doc, buffers, index: int, components: int) -> list[tuple[float, ...]]:
    values, actual = accessor_values(doc, buffers, index)
    if actual != components:
        raise ConversionError(f"accessor {index}: expected {components} components, got {actual}")
    return [tuple(values[i : i + components]) for i in range(0, len(values), components)]


def read_indices(doc, buffers, index: int) -> list[int]:
    values, components = accessor_values(doc, buffers, index)
    if components != 1:
        raise ConversionError(f"index accessor {index} is not SCALAR")
    return [int(v) for v in values]


# ── maths (row-vector matrices, matching System.Numerics) ──


def identity() -> list[list[float]]:
    return [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]


def mat_mul(a: list[list[float]], b: list[list[float]]) -> list[list[float]]:
    return [
        [sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)]
        for i in range(3)
    ]


def row_vec_mul(v: tuple[float, ...], m: list[list[float]]) -> tuple[float, ...]:
    return tuple(sum(v[k] * m[k][j] for k in range(3)) for j in range(3))


def quat_to_row_matrix(q: tuple[float, ...]) -> list[list[float]]:
    x, y, z, w = q
    # Transpose of the standard column-vector rotation matrix.
    return [
        [1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y + z * w), 2.0 * (x * z - y * w)],
        [2.0 * (x * y - z * w), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z + x * w)],
        [2.0 * (x * z + y * w), 2.0 * (y * z - x * w), 1.0 - 2.0 * (x * x + y * y)],
    ]


def matrix_to_quat(m: list[list[float]]) -> tuple[float, float, float, float]:
    # `m` is a column-vector rotation matrix.
    trace = m[0][0] + m[1][1] + m[2][2]
    if trace > 0.0:
        s = math.sqrt(trace + 1.0) * 2.0
        w = 0.25 * s
        x = (m[2][1] - m[1][2]) / s
        y = (m[0][2] - m[2][0]) / s
        z = (m[1][0] - m[0][1]) / s
    elif m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = math.sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2.0
        w = (m[2][1] - m[1][2]) / s
        x = 0.25 * s
        y = (m[0][1] + m[1][0]) / s
        z = (m[0][2] + m[2][0]) / s
    elif m[1][1] > m[2][2]:
        s = math.sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2.0
        w = (m[0][2] - m[2][0]) / s
        x = (m[0][1] + m[1][0]) / s
        y = 0.25 * s
        z = (m[1][2] + m[2][1]) / s
    else:
        s = math.sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2.0
        w = (m[1][0] - m[0][1]) / s
        x = (m[0][2] + m[2][0]) / s
        y = (m[1][2] + m[2][1]) / s
        z = 0.25 * s
    return normalize_quat((x, y, z, w))


def normalize_quat(q: tuple[float, ...]) -> tuple[float, float, float, float]:
    length = math.sqrt(sum(v * v for v in q))
    if length == 0.0:
        raise ConversionError("zero-length quaternion")
    return (q[0] / length, q[1] / length, q[2] / length, q[3] / length)


def quat_mul(a: tuple[float, ...], b: tuple[float, ...]) -> tuple[float, float, float, float]:
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def quat_conjugate(q: tuple[float, ...]) -> tuple[float, float, float, float]:
    return (-q[0], -q[1], -q[2], q[3])


def slerp(a: tuple[float, ...], b: tuple[float, ...], t: float) -> tuple[float, ...]:
    dot = sum(x * y for x, y in zip(a, b))
    if dot < 0.0:
        b = tuple(-v for v in b)
        dot = -dot
    if dot > 0.9995:
        return normalize_quat(tuple(x + (y - x) * t for x, y in zip(a, b)))
    theta = math.acos(max(-1.0, min(1.0, dot)))
    sin_theta = math.sin(theta)
    wa = math.sin((1.0 - t) * theta) / sin_theta
    wb = math.sin(t * theta) / sin_theta
    return normalize_quat(tuple(x * wa + y * wb for x, y in zip(a, b)))


def lerp3(a: tuple[float, ...], b: tuple[float, ...], t: float) -> tuple[float, ...]:
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# ── nodes ──


def node_rest_trs(node: dict[str, Any]) -> tuple[tuple[float, ...], tuple[float, ...], tuple[float, ...]]:
    if "matrix" in node:
        m = node["matrix"]  # column-major
        rotation = [
            [m[0], m[1], m[2]],
            [m[4], m[5], m[6]],
            [m[8], m[9], m[10]],
        ]
        translation = (m[12], m[13], m[14])
        return translate_rotate_scale(rotation, translation)
    return (
        tuple(node.get("translation", (0.0, 0.0, 0.0))),
        normalize_quat(tuple(node.get("rotation", (0.0, 0.0, 0.0, 1.0)))),
        tuple(node.get("scale", (1.0, 1.0, 1.0))),
    )


def translate_rotate_scale(
    rotation: list[list[float]], translation: tuple[float, ...]
) -> tuple[tuple[float, ...], tuple[float, ...], tuple[float, ...]]:
    """Decompose a row-vector linear part into (translation, rotation, scale)."""
    scales = []
    rows = []
    for row in rotation:
        length = math.sqrt(sum(v * v for v in row))
        if length == 0.0:
            raise ConversionError("node transform has a zero scale axis")
        scales.append(length)
        rows.append(tuple(v / length for v in row))
    for i in range(3):
        for j in range(i + 1, 3):
            if abs(sum(rows[i][k] * rows[j][k] for k in range(3))) > 1e-3:
                raise ConversionError(
                    "node transform has shear; only rotation/scale/translation are supported"
                )
    # `rows` is the row-vector matrix; transpose for the quaternion extractor.
    column_matrix = [[rows[j][i] for j in range(3)] for i in range(3)]
    return translation, matrix_to_quat(column_matrix), tuple(scales)


def trs_matrix(
    translation: tuple[float, ...], rotation: tuple[float, ...], scale: tuple[float, ...]
) -> list[list[float]]:
    r = quat_to_row_matrix(rotation)
    return [[r[i][j] * scale[i] for j in range(3)] for i in range(3)]


def compose(
    a: tuple[list[list[float]], tuple[float, ...]],
    b: tuple[list[list[float]], tuple[float, ...]],
) -> tuple[list[list[float]], tuple[float, ...]]:
    """Row-vector composition: apply `a`, then `b`."""
    rotated = row_vec_mul(a[1], b[0])
    return mat_mul(a[0], b[0]), tuple(rotated[i] + b[1][i] for i in range(3))


def node_parents(doc: dict[str, Any]) -> dict[int, int]:
    parents: dict[int, int] = {}
    for index, node in enumerate(doc.get("nodes", [])):
        for child in node.get("children", []):
            parents[child] = index
    return parents


# ── axis conversion ──


def convert_vector(v: tuple[float, ...]) -> tuple[float, ...]:
    return (v[0], -v[2], v[1])


def convert_quat(q: tuple[float, ...]) -> tuple[float, float, float, float]:
    return normalize_quat(quat_mul(quat_mul(AXIS_QUAT, q), quat_conjugate(AXIS_QUAT)))


def convert_local(
    translation: tuple[float, ...],
    rotation: tuple[float, ...],
    scale: tuple[float, ...],
) -> tuple[tuple[float, ...], tuple[float, ...], tuple[float, ...]]:
    return convert_vector(translation), convert_quat(rotation), scale


# ── animations ──


class Clip:
    def __init__(self, name: str, times: list[float], channels: dict[int, dict[str, Any]]):
        self.name = name
        self.times = times
        self.channels = channels


def read_clips(doc, buffers) -> list[Clip]:
    clips: list[Clip] = []
    for animation in doc.get("animations", []):
        channels: dict[int, dict[str, Any]] = {}
        all_times: set[float] = set()
        for channel in animation.get("channels", []):
            target = channel["target"]
            path = target["path"]
            if path not in ("translation", "rotation", "scale"):
                continue
            sampler = animation["samplers"][channel["sampler"]]
            interpolation = sampler.get("interpolation", "LINEAR")
            if interpolation == "CUBICSPLINE":
                raise ConversionError(
                    f"animation '{animation.get('name', '')}': CUBICSPLINE is unsupported"
                )
            times = [float(v[0]) for v in read_vec(doc, buffers, sampler["input"], 1)]
            components = 4 if path == "rotation" else 3
            values = read_vec(doc, buffers, sampler["output"], components)
            channels.setdefault(target["node"], {})[path] = (
                times,
                values,
                interpolation,
            )
            all_times.update(round(t, 6) for t in times)
        if not channels:
            continue
        times = sorted(all_times)
        clips.append(Clip(animation.get("name", f"Anim_{len(clips)}"), times, channels))
    return clips


def sample_channel(
    channel: dict[str, Any], path: str, time: float
) -> tuple[float, ...]:
    times, values, interpolation = channel
    index = bisect_right(times, time) - 1
    if index < 0:
        return values[0]
    if index >= len(times) - 1:
        return values[-1]
    span = times[index + 1] - times[index]
    blend = 0.0 if span <= 0.0 else (time - times[index]) / span
    if interpolation == "STEP" or blend <= 0.0:
        return values[index]
    if path == "rotation":
        return slerp(values[index], values[index + 1], blend)
    return lerp3(values[index], values[index + 1], blend)


def node_trs_at(
    node: dict[str, Any],
    channels: dict[str, Any] | None,
    time: float,
) -> tuple[tuple[float, ...], tuple[float, ...], tuple[float, ...]]:
    translation, rotation, scale = node_rest_trs(node)
    if channels is not None:
        if "translation" in channels:
            translation = sample_channel(channels["translation"], "translation", time)
        if "rotation" in channels:
            rotation = normalize_quat(sample_channel(channels["rotation"], "rotation", time))
        if "scale" in channels:
            scale = sample_channel(channels["scale"], "scale", time)
    return translation, rotation, scale


# ── converter ──


def texture_name(doc: dict[str, Any], material_index: int) -> str:
    if material_index < 0 or material_index >= len(doc.get("materials", [])):
        return ""
    material = doc["materials"][material_index]
    reference = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
    if reference is None:
        return ""
    texture = doc["textures"][reference["index"]]
    image = doc["images"][texture["source"]]
    uri = image.get("uri", "")
    if not uri or uri.startswith("data:"):
        raise ConversionError("material base-colour image is not a file URI")
    return Path(uri).name


def mesh_node_names(doc: dict[str, Any]) -> dict[int, str]:
    names: dict[int, str] = {}
    for node in doc.get("nodes", []):
        if "mesh" in node and node["mesh"] not in names:
            names[node["mesh"]] = node.get("name", "")
    return names


def convert_meshes(doc, buffers, joint_remap):
    positions: list[tuple[float, ...]] = []
    texcoords: list[tuple[float, ...]] = []
    normals: list[tuple[float, ...]] = []
    blend_indices: list[tuple[int, ...]] = []
    blend_weights: list[tuple[int, ...]] = []
    triangles: list[int] = []
    meshes: list[dict[str, Any]] = []
    node_names = mesh_node_names(doc)

    vertex_base = 0
    triangle_base = 0
    for mesh_index, mesh in enumerate(doc.get("meshes", [])):
        fallback_name = mesh.get("name", f"Mesh_{mesh_index}").split(" ")[0]
        name = node_names.get(mesh_index, fallback_name) or fallback_name
        for primitive in mesh.get("primitives", []):
            if primitive.get("mode", 4) != 4:
                raise ConversionError(f"mesh '{name}': only TRIANGLES primitives are supported")
            attributes = primitive.get("attributes", {})
            for required in ("POSITION", "NORMAL", "TEXCOORD_0", "JOINTS_0", "WEIGHTS_0"):
                if required not in attributes:
                    raise ConversionError(f"mesh '{name}': missing {required} attribute")

            primitive_positions = [
                convert_vector(v) for v in read_vec(doc, buffers, attributes["POSITION"], 3)
            ]
            vertex_count = len(primitive_positions)
            if vertex_count == 0:
                continue
            primitive_normals = [
                convert_vector(v) for v in read_vec(doc, buffers, attributes["NORMAL"], 3)
            ]
            # IQM's V matches glTF's: the Blender exporter flips Blender's
            # bottom-left V to the top-left convention, which glTF already uses.
            primitive_uvs = read_vec(doc, buffers, attributes["TEXCOORD_0"], 2)
            joint_values = read_vec(doc, buffers, attributes["JOINTS_0"], 4)
            weight_values = read_vec(doc, buffers, attributes["WEIGHTS_0"], 4)
            if len(primitive_normals) != vertex_count or len(primitive_uvs) != vertex_count:
                raise ConversionError(f"mesh '{name}': attribute vertex counts differ")

            for j in range(vertex_count):
                weights = weight_values[j]
                if sum(weights) <= 0.0:
                    raise ConversionError(f"mesh '{name}': vertex {j} has no skin weights")
                scaled = [min(255, max(0, int(round(w * 255.0)))) for w in weights]
                difference = 255 - sum(scaled)
                if difference != 0:
                    largest = max(range(4), key=lambda i: weights[i])
                    scaled[largest] = min(255, max(0, scaled[largest] + difference))
                if sum(scaled) != 255:
                    raise ConversionError(f"mesh '{name}': vertex {j} weights do not normalize")
                blend_weights.append(tuple(scaled))
                try:
                    # JOINTS_0 holds indices into skin.joints, not node indices.
                    blend_indices.append(
                        tuple(joint_remap[int(v)] for v in joint_values[j])
                    )
                except KeyError:
                    raise ConversionError(
                        f"mesh '{name}': vertex {j} references a joint outside the skin"
                    ) from None

            indices_ref = primitive.get("indices")
            indices = (
                list(range(vertex_count))
                if indices_ref is None
                else read_indices(doc, buffers, indices_ref)
            )
            if len(indices) % 3 != 0:
                raise ConversionError(f"mesh '{name}': index count is not a multiple of 3")
            if indices and max(indices) >= vertex_count:
                raise ConversionError(f"mesh '{name}': index outside the primitive")
            # IQM uses the reversed (Quake) winding relative to glTF/Blender. The
            # surface pipeline does not cull, but the outline pipeline culls front
            # faces, so an unflipped winding draws the hull over the model.
            for k in range(0, len(indices), 3):
                a, b, c = indices[k : k + 3]
                triangles.extend((vertex_base + a, vertex_base + c, vertex_base + b))

            meshes.append(
                {
                    "name": name,
                    "material": texture_name(doc, primitive.get("material", -1)),
                    "first_vertex": vertex_base,
                    "num_vertices": vertex_count,
                    "first_triangle": triangle_base,
                    "num_triangles": len(indices) // 3,
                }
            )
            positions.extend(primitive_positions)
            normals.extend(primitive_normals)
            texcoords.extend(primitive_uvs)
            vertex_base += vertex_count
            triangle_base += len(indices) // 3

    return meshes, positions, texcoords, normals, blend_indices, blend_weights, triangles


def convert(doc, buffers, include_animations: bool):
    skins = doc.get("skins", [])
    if len(skins) != 1:
        raise ConversionError(f"expected exactly one skin, found {len(skins)}")
    skin = skins[0]
    parents = node_parents(doc)
    nodes = doc["nodes"]

    # ── joints, parent-before-child ──
    skin_joints = [int(j) for j in skin["joints"]]
    if len(set(skin_joints)) != len(skin_joints):
        raise ConversionError("skin lists a joint twice")
    if len(skin_joints) > 255:
        raise ConversionError("IQM blend indices are ubyte; the skin has more than 255 joints")

    joint_of_node = {node: index for index, node in enumerate(skin_joints)}
    depth: list[int] = []
    for node in skin_joints:
        level = 0
        ancestor = parents.get(node)
        while ancestor is not None:
            level += 1
            ancestor = parents.get(ancestor)
        depth.append(level)
    order = sorted(range(len(skin_joints)), key=lambda i: (depth[i], i))
    reordered_nodes = [skin_joints[i] for i in order]
    old_to_new = {old: new for new, old in enumerate(order)}
    joints_by_node = {node: old_to_new[i] for i, node in enumerate(skin_joints)}
    joint_parents: list[int] = []
    for node in reordered_nodes:
        ancestor = parents.get(node)
        while ancestor is not None and ancestor not in joint_of_node:
            ancestor = parents.get(ancestor)
        joint_parents.append(joints_by_node[ancestor] if ancestor is not None else -1)

    def ancestor_chain(node: int) -> list[int]:
        """Nodes from the nearest ancestor joint (exclusive) down to `node`."""
        chain = []
        current = node
        while current is not None:
            chain.append(current)
            parent = parents.get(current)
            if parent is None or parent in joint_of_node:
                break
            current = parent
        return list(reversed(chain))

    def local_at(node: int, channels: dict[int, dict[str, Any]], time: float):
        matrix = identity()
        position = (0.0, 0.0, 0.0)
        for part in ancestor_chain(node):
            translation, rotation, scale = node_trs_at(
                nodes[part], channels.get(part), time
            )
            matrix, position = compose(
                (matrix, position),
                (trs_matrix(translation, rotation, scale), translation),
            )
        return translate_rotate_scale(matrix, position)

    # ── rest joints ──
    joints = []
    rest_trs = []
    for node in reordered_nodes:
        t, r, s = local_at(node, {}, 0.0)
        t, r, s = convert_local(t, r, s)
        rest_trs.append((t, r, s))
        joints.append(
            {
                "name": nodes[node].get("name") or f"joint_{joints_by_node[node]}",
                "parent": joint_parents[len(joints)],
                "translate": t,
                "rotate": r,
                "scale": s,
            }
        )

    # ── clips ──
    joint_count = len(joints)
    clips = read_clips(doc, buffers) if include_animations else []
    anim_frames: list[dict[str, Any]] = []
    for clip in clips:
        if len(clip.times) < 1:
            continue
        deltas = [clip.times[i + 1] - clip.times[i] for i in range(len(clip.times) - 1)]
        if deltas:
            median = sorted(deltas)[len(deltas) // 2]
            fps = float(round(1.0 / median))
            if fps <= 0.0 or abs(1.0 / fps - median) > 1e-4:
                raise ConversionError(
                    f"animation '{clip.name}': key times are not uniformly spaced"
                )
        else:
            fps = 30.0

        frames = []
        # Quaternions have two representations for the same rotation. Anchor each
        # joint's first frame to its rest rotation and each later frame to the
        # joint's *own* previous frame, so quantized components stay continuous.
        previous_rotations = [rest_trs[i][1] for i in range(joint_count)]
        for time in clip.times:
            frame = []
            for index, node in enumerate(reordered_nodes):
                t, r, s = local_at(node, clip.channels, time)
                converted = convert_local(t, r, s)
                previous = previous_rotations[index]
                if sum(a * b for a, b in zip(converted[1], previous)) < 0.0:
                    converted = (converted[0], tuple(-v for v in converted[1]), converted[2])
                previous_rotations[index] = converted[1]
                frame.append(converted)
            frames.append(frame)
        anim_frames.append({"name": clip.name, "fps": fps, "frames": frames})

    # ── poses: shared channel ranges across every clip ──
    offsets = [[0.0] * CHANNEL_COUNT for _ in range(joint_count)]
    scales = [[0.0] * CHANNEL_COUNT for _ in range(joint_count)]
    for j in range(joint_count):
        for c in range(CHANNEL_COUNT):
            values = []
            for animation in anim_frames:
                for frame in animation["frames"]:
                    values.append(channel_value(frame[j], c))
            if not values:
                values = [channel_value(rest_trs[j], c)]
            low, high = min(values), max(values)
            offsets[j][c] = low
            span = high - low
            scales[j][c] = span / 65535.0 if span > 1e-10 else 0.0

    # ── animations in packed frame order ──
    animations = []
    frame_data: list[int] = []
    first_frame = 0
    for animation in anim_frames:
        animations.append(
            {
                "name": animation["name"],
                "first_frame": first_frame,
                "num_frames": len(animation["frames"]),
                "fps": animation["fps"],
            }
        )
        for frame in animation["frames"]:
            for j in range(joint_count):
                for c in range(CHANNEL_COUNT):
                    span = scales[j][c]
                    value = channel_value(frame[j], c)
                    quantized = (
                        0 if span == 0.0 else int(round((value - offsets[j][c]) / span))
                    )
                    frame_data.append(min(65535, max(0, quantized)))
        first_frame += len(animation["frames"])

    # ── geometry ──
    meshes, positions, texcoords, normals, blend_indices, blend_weights, triangles = (
        convert_meshes(doc, buffers, old_to_new)
    )

    return {
        "meshes": meshes,
        "positions": positions,
        "texcoords": texcoords,
        "normals": normals,
        "blend_indices": blend_indices,
        "blend_weights": blend_weights,
        "triangles": triangles,
        "joints": joints,
        "offsets": offsets,
        "scales": scales,
        "animations": animations,
        "frames": frame_data,
    }


def channel_value(trs, channel: int) -> float:
    translation, rotation, scale = trs
    if channel < 3:
        return translation[channel]
    if channel < 7:
        return rotation[channel - 3]
    return scale[channel - 7]


# ── IQM writing ──


class Writer:
    def __init__(self) -> None:
        self.data = bytearray()

    def align(self, alignment: int = 4) -> None:
        while len(self.data) % alignment:
            self.data.append(0)

    def u32(self, value: int) -> None:
        self.data += struct.pack("<I", value)

    def f32(self, value: float) -> None:
        self.data += struct.pack("<f", value)

    def u16(self, value: int) -> None:
        self.data += struct.pack("<H", value)

    def text(self, value: str) -> int:
        offset = len(self.data)
        self.data += value.encode("utf-8") + b"\x00"
        return offset


def write_iqm(model: dict[str, Any], out_path: Path) -> None:
    text = Writer()
    text_count = 0

    def add(value: str) -> int:
        nonlocal text_count
        text_count += 1
        return text.text(value)

    mesh_names = [add(m["name"]) for m in model["meshes"]]
    mesh_materials = [add(m["material"]) for m in model["meshes"]]
    joint_names = [add(j["name"]) for j in model["joints"]]
    anim_names = [add(a["name"]) for a in model["animations"]]

    # ── section bodies ──
    meshes = Writer()
    for record, name, material in zip(model["meshes"], mesh_names, mesh_materials):
        meshes.u32(name)
        meshes.u32(material)
        meshes.u32(record["first_vertex"])
        meshes.u32(record["num_vertices"])
        meshes.u32(record["first_triangle"])
        meshes.u32(record["num_triangles"])

    vertex_arrays = Writer()
    vertex_data = Writer()
    vertex_count = len(model["positions"])
    arrays = [
        (VERTEX_POSITION, VERTEX_FORMAT_FLOAT, 3, model["positions"]),
        (VERTEX_TEXCOORD, VERTEX_FORMAT_FLOAT, 2, model["texcoords"]),
        (VERTEX_NORMAL, VERTEX_FORMAT_FLOAT, 3, model["normals"]),
        (VERTEX_BLEND_INDICES, VERTEX_FORMAT_UBYTE, 4, model["blend_indices"]),
        (VERTEX_BLEND_WEIGHTS, VERTEX_FORMAT_UBYTE, 4, model["blend_weights"]),
    ]
    array_records = []
    for kind, fmt, size, values in arrays:
        vertex_data.align(4)
        offset = len(vertex_data.data)
        if fmt == VERTEX_FORMAT_FLOAT:
            for value in values:
                for component in value:
                    vertex_data.f32(float(component))
        else:
            for value in values:
                vertex_data.data += bytes(int(component) for component in value)
        array_records.append((kind, fmt, size, offset))

    for kind, fmt, size, offset in array_records:
        vertex_arrays.u32(kind)
        vertex_arrays.u32(0)
        vertex_arrays.u32(fmt)
        vertex_arrays.u32(size)
        vertex_arrays.u32(offset)

    triangles = Writer()
    for index in model["triangles"]:
        triangles.u32(index)

    joints = Writer()
    for index, joint in enumerate(model["joints"]):
        joints.u32(joint_names[index])
        joints.u32(joint["parent"] & 0xFFFFFFFF)
        for value in joint["translate"]:
            joints.f32(value)
        for value in joint["rotate"]:
            joints.f32(value)
        for value in joint["scale"]:
            joints.f32(value)

    poses = Writer()
    for index, joint in enumerate(model["joints"]):
        poses.u32(joint["parent"] & 0xFFFFFFFF)
        poses.u32(CHANNEL_MASK)
        for value in model["offsets"][index]:
            poses.f32(value)
        for value in model["scales"][index]:
            poses.f32(value)

    anims = Writer()
    for index, animation in enumerate(model["animations"]):
        anims.u32(anim_names[index])
        anims.u32(animation["first_frame"])
        anims.u32(animation["num_frames"])
        anims.f32(animation["fps"])
        anims.u32(0)

    frames = Writer()
    for value in model["frames"]:
        frames.u16(value)

    # ── lay the file out ──
    out = Writer()
    out.data += bytes(IQM_HEADER_SIZE)
    out.align(4)
    ofs_text = len(out.data)
    out.data += text.data
    out.align(4)

    ofs_meshes = len(out.data)
    out.data += meshes.data
    out.align(4)

    ofs_vertex_arrays = len(out.data)
    out.data += vertex_arrays.data
    out.align(4)

    # Vertex-array offsets are relative to the file, but were recorded before
    # the section was placed; rebase them now.
    vertex_data_offset = len(out.data)
    out.data += vertex_data.data
    out.align(4)
    for i in range(len(array_records)):
        position = ofs_vertex_arrays + i * 20 + 16
        struct.pack_into("<I", out.data, position, vertex_data_offset + array_records[i][3])

    ofs_triangles = len(out.data)
    out.data += triangles.data
    out.align(4)

    ofs_joints = len(out.data)
    out.data += joints.data
    out.align(4)

    ofs_poses = len(out.data)
    out.data += poses.data
    out.align(4)

    ofs_anims = len(out.data)
    out.data += anims.data
    out.align(4)

    ofs_frames = len(out.data)
    out.data += frames.data
    out.align(4)

    header = bytearray(IQM_HEADER_SIZE)
    header[:16] = IQM_MAGIC
    struct.pack_into("<I", header, 16, IQM_VERSION)
    struct.pack_into("<I", header, 20, len(out.data))
    struct.pack_into("<I", header, 24, 0)  # flags
    struct.pack_into("<I", header, 28, text_count)
    struct.pack_into("<I", header, 32, ofs_text)
    struct.pack_into("<I", header, 36, len(model["meshes"]))
    struct.pack_into("<I", header, 40, ofs_meshes)
    struct.pack_into("<I", header, 44, len(array_records))
    struct.pack_into("<I", header, 48, vertex_count)
    struct.pack_into("<I", header, 52, ofs_vertex_arrays)
    struct.pack_into("<I", header, 56, len(model["triangles"]) // 3)
    struct.pack_into("<I", header, 60, ofs_triangles)
    struct.pack_into("<I", header, 64, 0)  # adjacency
    struct.pack_into("<I", header, 68, len(model["joints"]))
    struct.pack_into("<I", header, 72, ofs_joints)
    struct.pack_into("<I", header, 76, len(model["joints"]))
    struct.pack_into("<I", header, 80, ofs_poses)
    struct.pack_into("<I", header, 84, len(model["animations"]))
    struct.pack_into("<I", header, 88, ofs_anims)
    struct.pack_into("<I", header, 92, len(model["frames"]) // (len(model["joints"]) * CHANNEL_COUNT) if model["joints"] else 0)
    struct.pack_into("<I", header, 96, len(model["joints"]) * CHANNEL_COUNT)
    struct.pack_into("<I", header, 100, ofs_frames)
    struct.pack_into("<I", header, 104, 0)  # bounds
    struct.pack_into("<I", header, 108, 0)  # comment count
    struct.pack_into("<I", header, 112, 0)  # comment offset
    struct.pack_into("<I", header, 116, 0)  # extension count
    struct.pack_into("<I", header, 120, 0)  # extension offset
    out.data[:IQM_HEADER_SIZE] = header

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(out.data)


def main() -> int:
    args = parse_args()
    input_path = Path(args.input)
    out_path = Path(args.out) if args.out else input_path.with_suffix(".iqm")

    try:
        doc, buffers = load_document(input_path)
        model = convert(doc, buffers, not args.no_animations)
        write_iqm(model, out_path)
    except ConversionError as exc:
        print(f"gltf_to_iqm: {exc}", file=sys.stderr)
        return 1

    joints = len(model["joints"])
    animations = len(model["animations"])
    frames = len(model["frames"]) // (joints * CHANNEL_COUNT) if joints else 0
    print(f"wrote {out_path} ({out_path.stat().st_size} bytes)")
    print(
        f"meshes: {len(model['meshes'])}, vertices: {len(model['positions'])}, "
        f"triangles: {len(model['triangles']) // 3}, joints: {joints}"
    )
    print(f"animations: {animations}, frames: {frames}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
