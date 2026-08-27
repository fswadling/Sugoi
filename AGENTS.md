# AGENTS.md

## Code Style

- Minimalism is favoured. Write only what is needed.
- Avoid unnecessary functions. Abide by the rule of three: introduce a
  function for reuse only when there are three call sites that need it, or
  when it is an obvious orchestrator function (e.g. a top-level `update`
  function that delegates to smaller systems).
- Avoid just-in-case fallbacks unless they are part of an expected use case.
- Prefer exceptions/asserts over silently returning defaults for unexpected
  behaviour or state. Use `Result`/`Option` for *expected* failure paths and
  `failwith`/`invalidOp`/`assert` for programming bugs.
- Bind before matching. Prefer binding a call's result to a variable, then
  `match`ing the variable, over matching an inline call:

  ```fsharp
  // prefer
  let result = Sdl.CreateWindow(title, w, h)
  match result with
  | Ok window -> ...
  | Error e -> ...

  // avoid
  match Sdl.CreateWindow(title, w, h) with
  | Ok window -> ...
  | Error e -> ...
  ```

- F# compiles files in the order they are listed in the `.fsproj`; a file can
  only reference symbols defined in earlier files. Keep dependencies
  one-directional.
- Prefer `[<Struct>]` records (and struct tuples) for hot-path data created and
  consumed every frame. Reserve classes for long-lived, shared state.
- This is primarily a data-driven project. Favour mutable data structures that
  keep related data contiguous for cache performance (arrays,
  structs-of-arrays, `Span`), and don't be afraid of simple imperative,
  procedural code on the hot path.
- Impure functions that return `unit` are fine; call them with `do` rather than
  forcing a value. Functional style (and the garbage collector) are welcome off
  the hot path, but per-frame code should avoid unnecessary allocations.
- Use intention-block comments. Annotate a logical block with a short comment
  stating its *intent* (what it accomplishes / why), not a restatement of the
  code. Use the `// ── … ──` banner style so sections are easy to scan.

## Spec-Driven Development

This project uses Spec Kit ("speckit"). New features start as a spec in
`specs/<NNN>-<name>/spec.md`, then move through plan, tasks, implement, and
converge. Before writing engine code, check the active feature directory in
`.specify/feature.json` (or `SPECIFY_FEATURE_DIRECTORY`).
