# Phase 0 Research: Face Light Fade

The spec left no `NEEDS CLARIFICATION` — `/speckit-clarify` resolved the two open points (the sign
and the comparison axis). What remains is where the fade is computed and how. Each decision below
names the requirement it serves.

## D1. The fade is computed in the fragment shader, not on the CPU draw path

- **Decision**: Derive the fade factor inside `shaders/cel.frag`, from the uniform vectors already
  pushed (`cel.uFace.xyz`, `cel.uLightDir.xyz`).
- **Rationale**: The shader already has both inputs. Computing it on the CPU would mean either a new
  uniform field (growing the 80-byte block and editing `Mesh.draw`/`Mesh.fs`) or reusing a channel,
  both of which add host-side code and break the feature's "no new data, no new uniform field"
  stance (FR-006/FR-007). The fade is a pure function of values already at the fragment, so it
  belongs there.
- **Alternatives considered**: (a) Compute a scalar in `ActorRender.draw` and push it — needs a new
  uniform slot, a new `Mesh.draw` parameter and a wider byte array; rejected as more moving parts for
  no benefit. (b) Fold it into `faceLightDir` so the idealized direction itself rotates — rejected,
  because that would move the *band boundary* (violating FR-001) instead of scaling its amount.

## D2. The comparison is azimuth-only, with a fallback on degenerate input

- **Decision**: Build `faceH = (uFace.x, 0, uFace.z)` and `sunH = (uLightDir.x, 0, uLightDir.z)`;
  `toward = dot(faceH, sunH) / (|faceH| · |sunH|)` when both lengths exceed a small epsilon, else
  `toward = 1` (full front light).
- **Rationale**: The spec's clarification fixed the axis as azimuth only (FR-003), so a turn through
  a circle reads the same whatever the sun's elevation. The epsilon guard covers a sun at the zenith
  (its horizontal part vanishes) and a facing with no horizontal component; in both there is no
  azimuth to compare, so the face keeps its full front light rather than dividing by zero (the
  spec's edge cases).
- **Alternatives considered**: A full 3-D `dot` — rejected in clarification (the swing would shrink
  as the sun rose). Comparing already-normalized values — the face direction's horizontal part is
  unit by construction, but the sun's is not (it shortens with elevation), so both are normalized
  explicitly for clarity and safety.

## D3. Sign: facing the sun is bright

- **Decision**: The lit band is untouched when the actor's facing is **aligned** with
  `Light.Direction` and switched to the shadow band when **anti-aligned**.
- **Rationale**: `Texture.Light.Direction` is documented as pointing *toward* the sun
  (`specs/043`), and the spec's clarification chose the physical sense — a face turned into the sun
  is lit, a face turned away is in shade — matching the artefact to fix (a face staying fully lit
  with the sun behind it) and the body's own shading.
- **Alternatives considered**: The literal "opposite to the light direction = full brightness"
  reading, which — once the light's *travel* direction (the opposite of `Light.Direction`) is
  accounted for — is the same physical sense; the clarification pinned the canonical wording.

## D4. The fade is a two-tone switch at the 90° crossing

- **Decision**: With `toward = n̂ · ŝ` (the azimuthal dot), the factor is
  `smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward)`, with `FACE_FADE_SOFTNESS = 0.05`.
- **Rationale**: The spec's later clarification chose a *switch* (FR-002): the face reads as the
  crisp two-tone the rest of the character shows, decided by which side of the light it is on, rather
  than a soft gradient. `smoothstep` is GLSL's built-in eased edge — already used for the band ramp —
  so the switch costs one call and one constant. A half-width of `0.05` in the dot (≈ ±3° about the
  90° crossing) matches the band ramp's crispness.
- **Alternatives considered**: The earlier linear-in-cosine lerp (`(cos θ + 1) / 2`) — rejected on
  review: a gradual gradient looked wrong against the two-tone body and environment. A hard
  `step(0.0, toward)` — rejected: no easing at the crossing reads as a strobing, aliased edge.

## D5. The fade multiplies the band

- **Decision**: `band *= smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward)` for face
  fragments, before the existing additive composition.
- **Rationale**: The colour is `albedo * (ambient + sunColour * band)`. Scaling `band` by the factor
  `f` gives `albedo * (ambient + sunColour * band * f)`: the lit band (`band = 1`) becomes
  `albedo * (ambient + sunColour * f)`, which at `f = 1` is the untouched full front light and at
  `f = 0` is exactly the shadow band (`albedo * ambient`). That is precisely "the lit band switched to
  the shadow band's brightness on the away side", and it leaves the band *colours* and the additive
  shape untouched (FR-004). The band's direction and the ramp are unmodified (FR-001).
- **Alternatives considered**: Lerping the whole `color` toward `albedo * ambient` — equivalent to
  the above for the lit band and identical for the shadow band, but it touches the composed colour
  and so reads as a larger change than it is; scaling `band` keeps the change local to the term the
  spec names.

## D6. `specs/040` is referenced, not edited

- **Decision**: This feature documents the fade in its own spec and leaves
  `specs/040-actor-cel-shading/` untouched.
- **Rationale**: `specs/040`'s band *direction* contract (FR-007) still holds; this feature only
  scales the band's amount, which is a refinement of its FR-008 ("only direction is idealized")
  rather than a contradiction of it. Recording it as a separate, later feature keeps the shipped
  spec's history intact and makes the port-only extension explicit.
