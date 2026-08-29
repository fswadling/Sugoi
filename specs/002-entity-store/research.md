# Research: Entity Store

Decisions resolving the open technical choices for this feature.

## Decision: generic `SlotMap<'T>` backed by a fixed `[<Struct>]` array, struct-only

- **Decision**: one generic `SlotMap<'T when 'T : struct>` over payload type
  `'T`, with a fixed `Slot<'T> array` plus a `Count` and a free-list head.
  `Slot<'T>` is a struct holding `Value`, `Generation`, `Alive`, and `NextFree`.
  The `'T : struct` constraint restricts payloads to value types.
- **Rationale**: mirrors `docs/ARCHITECTURE.md` (one slot map per collection, no
  ECS framework) and keeps entity storage inline and cache-friendly. Genericity
  avoids five copy-pasted maps. The struct-only constraint is enforced because a
  slot map of reference types would store pointers, not contiguous data — it
  defeats the whole locality purpose.
- **Alternatives considered**: a single non-generic `Entity`-only map (rejected —
  zones need distinct `Actor`/`Prop`/`Waypoint`/`Trigger` maps); `System.Collections`
  dictionaries (rejected — heap-per-entry and no fixed-size/locality guarantee);
  an unconstrained `'T` (rejected — would allow reference-type payloads that
  break contiguity).

## Decision: two handles — generic `Handle` and domain `EntityRef`

- **Decision**: the generic slot map uses `Handle { Index; Generation }` for O(1)
  access and stale detection. The domain-level `EntityRef { Kind; Index }`
  (plus a `NO_ENTITY` sentinel) selects *which* slot map via `Kind` and the slot
  via `Index`, matching `docs/ARCHITECTURE.md`.
- **Rationale**: the slot map's stale detection is generic; the entity reference
  is the editor/systems' cross-kind handle. Keeping `EntityRef` free of a
  generation matches the architecture and defers entity-level staleness to when
  refs are actually resolved (phase 2b+).
- **Alternatives considered**: fold generation into `EntityRef` (rejected — the
  architecture specifies `{ kind, index }`, and it would couple the domain handle
  to one slot-map implementation).

## Decision: entities compose a nested `Entity` base field

- **Decision**: `Actor`, `Environment`, and `Prop` each embed a `Base : Entity`
  field; `Waypoint`/`Trigger` are standalone (no base), per the architecture.
- **Rationale**: F# has no struct inheritance, so a nested `Entity` reproduces
  Odin's `using entity: Entity` composition without duplication.
- **Alternatives considered**: a single `Entity` record with an optional
  kind-specific payload (rejected — would force a big union and lose the
  per-kind slot-map locality); `[<AbstractClass>]` reference types (rejected —
  heap-per-entity, fights the data-oriented goal).

## Decision: use `System.Numerics` for vector/quaternion/matrix math

- **Decision**: use `System.Numerics.Vector3`/`Quaternion`/`Matrix4x4` for the
  entity `Transform` (`Position`/`Rotation`/`Scale`) and all later math. No
  hand-rolled `Vec3`/`Quat`/`Mat4`.
- **Rationale**: these are BCL types (no NuGet dependency), SIMD-accelerated,
  value types (no GC pressure), and already tested. Their field names (`X`/`Y`/
  `Z`/`W`) already match the project's PascalCase convention.
- **Alternatives considered**: hand-rolled `[<Struct>]` `Vec3`/`Quat`/`Mat4`
  (rejected — reimplements and must be maintained/tested; the only benefit is
  layout control, which we do not need); a math NuGet package (rejected —
  unnecessary when the BCL provides it).
- **Caveat**: `Matrix4x4` is row-major/row-vector; keep the convention consistent
  with SDL_gpu (transpose as needed when uploading to shaders — phase 3).

## Decision: xUnit in a sibling test project

- **Decision**: a `fsharp-game.Tests` project (xUnit) references the main
  `fsharp-game.fsproj`; tests live in flat `test/<Module>Tests.fs` files.
- **Rationale**: xUnit is the default .NET test framework and `dotnet test` runs
  it directly; the main project stays an `Exe`. This matches the
  `test/<Module>Tests.fs` convention in `docs/PROJECT_STRUCTURE.md`.
- **Alternatives considered**: hand-rolled assert executable (rejected — loses
  standard test tooling); in-place `assert` calls in `core/` (rejected — pollutes
  the library with test code and no `dotnet test` integration).
