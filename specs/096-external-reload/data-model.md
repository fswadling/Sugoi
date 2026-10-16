# Data Model: Live Reload of the Project File

## No new data

The feature persists nothing and adds no persisted field. It adds one **transient editor boolean** and two
**module-private values** to the existing `world/Project.fs` store. The reloaded value is the same `Registry` the
project file already holds (`specs/095`).

## `world/Project.fs` — the factored helpers and the poll

```fsharp
// factored out of the existing loadFrom/saveTo so the two policies can share one read/decode/write
let private readText (path: string) : string option =        // absent -> None
    if File.Exists path then Some (File.ReadAllText path) else None

let private tryDecode (text: string) : Result<Registry, string> =  // illegal text -> Error (its message)
    try Ok (decode text) with ex -> Error ex.Message

let private writeAtomic (path: string) (text: string) : bool // .tmp + rename; reports a failure; true when written

// the startup policy, shared by loadFrom and load: a good file's library, or a reported empty one
let private decodeStartup (path: string) (text: string) : Registry

// the store gains a constant and two values beside `lastWritten`
let private pollSeconds = 0.25                                // the interval gate (~250 ms, while editing)

let mutable private lastText : string = ""                    // the file content the game last wrote or read
let mutable private accumulator : float = 0.0                 // the interval gate

/// The reloaded library when an *external* change lands, else None (no change / mid-write / absent).
let pollExternal (dt: float) : Registry option
```

`loadFrom` is re-expressed over `readText`/`tryDecode` **unchanged in behaviour** (absent → empty, no report;
damaged → reported + empty). `saveTo path registry = writeAtomic path (encode registry) |> ignore` — also unchanged in
behaviour. `load` and `persist` additionally maintain `lastText`:

| Function | `lastText` update |
|----------|-------------------|
| `load ()` | set to the file's text as read (even when it failed to decode), so a later edit is what triggers a reload |
| `persist registry` | set to the encoded text **only when `writeAtomic` reports success** (D3) — so a failed write is not mistaken for the file's content |

## The poll, step by step

```text
pollExternal dt:
    accumulator <- accumulator + dt
    if accumulator < pollSeconds then None                       # the gate: not yet
    else
    accumulator <- 0.0
    match readText projectPath with
    | None -> None                                               # absent / deleted: keep the running library
    | Some text ->
        if text = lastText then None                             # our own write, or a no-op rewrite
        else
        match tryDecode text with
        | Some registry ->                                       # an external change
            lastText <- text; lastWritten <- registry
            Some registry
        | None ->                                                # mid-write / damaged: keep the running library
            do Diagnostics.reportf "project: '%s' changed but could not be read; keeping the running library" projectPath
            None                                                 # lastText is NOT updated: retry next interval
```

## The editor toggle

```fsharp
// state/EditorState.fs — a transient editor boolean, beside Snap/Tool (not persisted, not a step)
LiveReload : bool        // default true; initialised in `create`

// editor/MiscPanel.fs — drawn after the Fade slider
if ImGui.checkbox ("Live reload", &state.Editor.LiveReload) then ()   // in-place write; no Command, no Undo step
```

## The drain

```fsharp
// world/Project.fs — the pure gate (spec FR-008): the poll runs only while editing, and only when enabled
let pollEnabled (editing: bool) (liveReload: bool) : bool = editing && liveReload

// screen/GameplayScreen.fs — end of `update`, after the existing `Project.persist state.Scenarios`
if Project.pollEnabled state.Editing state.Editor.LiveReload then
    match Project.pollExternal frame.Dt with
    | Some registry -> state.Scenarios <- registry
    | None -> ()
```

## Validation rules

- **Content-based**: a change is `readText path <> lastText`; timestamps and file length are never consulted (D1, D2).
- **Own writes are invisible**: `persist` sets `lastText` on success, so the poll never reloads the game's own save.
- **Runtime is non-destructive**: an absent, deleted, or undecodable file installs nothing and reports where due; the
  running library is preserved (D4). This is the deliberate opposite of the startup policy in `loadFrom`.
- **Retry**: a damaged read leaves `lastText` unchanged, so the next interval re-reads and picks up the finished file.
- **Gated**: at most one read per `pollSeconds` (~250 ms) **and only while the author is editing**, driven by the
  `dt` the caller supplies; `Project.pollEnabled editing liveReload` is the (pure, tested) gate that a caller applies.
- **Nothing else moves**: installing a reloaded library writes `state.Scenarios` and nothing else (D5).
