# Quickstart: Bounded environment residency

Prerequisites: a Linux desktop with the project's usual native libraries; the repo builds and
runs as today (`dotnet build`, `dotnet run`, `dotnet test`). Environment assets are the two
shipped ones (`ArtRoom`, `Island`) unless noted.

## 1. Headless validation (no GPU required)

```
dotnet test
```

Expected: the full suite passes. What this feature adds or changes there:

- **`test/LruTests.fs` (new)** — `touch` moves a key to the head and adds it when absent;
  `tail` is the least recently used; `remove` and `contains`; `count` tracks membership.
  Deterministic, no device.
- **`test/AssetsTests.fs` (extended)** — `isEnvironmentResident` is false for a not-loaded
  asset and true for a loaded one; `evictionCandidates` walks the LRU order from the tail
  (least recently used first) and skips every id in `protectedIds`, so a protected asset is never
  offered for eviction; a metadata-only scan records an environment against a **missing** model
  file without reading it (`FR-010`), leaving `Loaded = None` and the cache empty.
- **`test/GameplayStateTests.fs` (extended)** — a switch to a zone whose environment asset is
  absent **raises and leaves `ActiveZone` unchanged** (`FR-017`).
- **Fixtures** — `test/TestSupport.fs` builds assets in the **loaded** shape, so
  `GameplayState.switchZone`, `Zone.loadEnvironment` and `History.undo` keep running
  device-free and behave exactly as before (`FR-006`, `SC-003`).

The texture refcount, the parse/upload and the glyph are not asserted here — this port cannot
open a device in tests — and are covered by the walkthrough below.

## 2. Startup performs no environment work

Run the app and enter gameplay. Expected:

- No environment model is read, parsed, or uploaded at launch; the first environment appears
  only when a zone with an environment is switched to (`FR-010`, `SC-005`). Confirm by
  temporarily logging inside the parse/upload step and observing no log before the first switch.
- The asset browser and the zone registry still list every environment by name (metadata only).

## 3. The Outliner glyph (FR-013–FR-016, SC-007)

Open the Outliner's **Zones** section. Expected:

- Each zone with an environment shows a residency glyph beside its name: `[+]` when its
  environment is loaded, `[ ]` when it is not, `[-]` when the zone has no environment.
- The active zone's environment reads `[+]` (`FR-016`).
- Switch to a zone whose environment is not loaded: the glyph for the incoming zone becomes
  `[+]` and any zone evicted to make room becomes `[ ]`, with no manual refresh (`FR-014`).
  Two zones sharing one environment asset show the same glyph.
- Clicking a zone row still switches/selects it and the delete control still works.

## 4. The budget holds under thrash (FR-001, FR-004, SC-001)

Because only two environments ship, exercise eviction by lowering the budget for the test:

1. Temporarily set `ENVIRONMENT_BUDGET_BYTES` below one environment (e.g. `256L * 1024L * 1024L`).
2. Alternate between two zones with different environments in the Outliner several times.
   Expected: each visit reloads the incoming environment (re-parse + re-upload) and renders it
   correctly; the previous environment's glyph returns to `[ ]`; nothing renders blank or wrong;
   the active zone always reads `[+]`. Report honestly that eviction was tested this way.
3. Watch process memory: after visiting several environments, resident memory should sit at the
   loaded set, not at "everything ever visited" — an eviction drops geometry as well as GPU data
   (`FR-001`, `FR-006`).
4. Restore the constant to `2 GB`.

Confirm physics and picking inside an environment work immediately after each switch — a
collider is built at load time and persists in the physics world across an eviction (`FR-006`,
`SC-004`).

## 5. A broken asset cancels cleanly (FR-017, SC-009)

Temporarily rename one environment's `.gltf` (or make a texture reference unresolvable) and
switch to a zone using it. Expected: the switch does **not** happen — the current zone stays
active and the app surfaces the failure loudly — rather than moving to a zone with no geometry
(`FR-017`). Restore the file; the switch then succeeds on retry.

## 6. Restart and shutdown (FR-012)

- Visit several environments, then switch to a zone with no environment and quit. Expected:
  clean shutdown, every loaded environment's buffers and textures released exactly once, no SDL
  handle warning and no double-release (`Gpu.destroy` clean).
- Relaunch and confirm startup is again metadata-only (step 2).

## Traceability

| Spec | Where proven |
|---|---|
| FR-001, FR-004, SC-001 | §4 (low-budget thrash, memory held) |
| FR-002, FR-003, FR-007, FR-008 | §3, §4 |
| FR-005 | §4.3 (active never evicted) |
| FR-006, SC-003, SC-004 | §1, §4 |
| FR-009 | §4.1 |
| FR-010, SC-005 | §2, §6 |
| FR-011 | asserted by the resident accessors; §4 shows no blank render |
| FR-012 | §6 |
| FR-013–FR-016, SC-007, SC-008 | §3 |
| FR-017, SC-009 | §1, §5 |
| FR-018, SC-010 | §4 (shared texture), §6 |
| SC-002 | §3, §4 |
| SC-006 | §4 (repeat), §6 |
