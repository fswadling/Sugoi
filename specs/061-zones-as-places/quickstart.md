# Quickstart: Zones as Places

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green. The tests that named zones by string now name
places; no device is opened.

## Manual

1. **Start**: on launch the game is at the designated start place, shown marked in the Outliner's **Zones**
   section (the only present place).
2. **Add a place**: open the Zones add control; it offers the absent declared places (e.g. `ArtRoom Evening`,
   `Island`). Choose one — it becomes present and active, and the previous place stays present.
3. **Author and save**: with the new place active, load an environment and place props, then use the **Zone
   Registry** panel's **Register** on that place — the setup is saved as its template.
4. **Switch**: click another present place in Zones; it becomes active as one undo step; Ctrl+Z returns to the
   previous active place.
5. **Remove a place**: the **Del** control is disabled for the active place. Switch away first, then remove —
   the place becomes absent, its resources are released, and (if nothing else uses it) its environment
   unloads (watch the residency marker / memory).
6. **Instantiate**: register a template at a place, make it absent, then **Instantiate** it: the place becomes
   present and active with its template loaded, as one step; one undo reverses it.
7. **Cue**: add a **Zone** node to the cue; it now offers a place chooser. Run the cue and confirm the active
   place changes to the chosen place.
8. **No free names**: there is no text field anywhere to type a zone name; only declared places can be chosen.

## References

- Identity contract: [contracts/place-identity.md](./contracts/place-identity.md)
- Data model: [data-model.md](./data-model.md)
