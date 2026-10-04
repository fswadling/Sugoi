# Quickstart: Zone Cue Ops

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green. The new cases are exercised through the cue
engine and the gameplay state with no device opened: present a place from a registered template and assert its
content and that the active place is unchanged; remove a present place and assert it is absent; preload a
zone's environment and assert the request is made without the active zone's environment being released; and
assert every redundant op performs nothing rather than raising.

## Manual

1. **Stream a place in**: author a cue with an **Add zone** op naming a declared place that is *absent* and
   has a registered template. Run it (Play). The Outliner's **Zones** section gains that place, but the active
   place does not change and the camera does not move — the op prepared the zone without entering it.
2. **Enter it**: follow the add with a **Set zone** op naming the same place. On Play the active place moves
   there. If the add (or a preceding preload) had already loaded the environment, the geometry is already
   present — no pop-in.
3. **Preload the next room**: author a **Preload zone** op naming a present, inactive place, then a telegraphed
   delay (a `Wait` or two). On Play, watch that place's environment-residency marker go `[~]` (loading) then
   `[+]` (resident) while the player is still in the current place; then **Set zone** to it. A place added
   empty from the editor but **registered** is readied too: the preload streams its template in before loading,
   so you can add a bare room and still preload it from a cue.
4. **Remove a place**: author a **Remove zone** op naming a present, inactive place. On Play the place leaves
   the Zones list and its environment unloads (its marker disappears) unless another present place shares it.
5. **No-op re-runs**: run the same cue twice. The second run adds an already-present place, removes an
   already-absent one, and preloads a place with neither an environment nor a registered template — each op
   performs nothing and the cue completes, rather than faulting on state the first run produced.
6. **Authoring**: in the cue panel, each zone op offers a **place chooser** (the declared places), never a text
   field. Each accepted edit is one undo step.

## References

- Op contract: [contracts/zone-cue-ops.md](./contracts/zone-cue-ops.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
