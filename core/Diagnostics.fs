module Diagnostics

// A process-global tally of the app's non-fatal reports (the messages the hardening work routes through
// here), so errors are visible in the UI and not only in a terminal — a console log is easy to miss.
//
// Mirrors the `Gpu`/`Texture` stats pattern: the deep layers (`world`/`state`/`editor`/`render`) report
// here instead of writing to the console directly, and a display function reads the tally. Reports are
// de-duplicated by their text, so one that repeats every frame (a re-fired cue, a held key) reads as a
// single entry carrying its count rather than flooding the view.

// Saturate well below overflow, so a runaway report loop cannot wrap the tally.
let private maxCount = 1_000_000

// How many distinct reports are remembered for display.
let private maxEntries = 32

/// Past this many reports, the UI escalates the indicator so the accumulation cannot be missed.
let loudThreshold = 10

/// One report: its text and how many times it has occurred.
type Report =
    { Message : string
      mutable Count : int }

// The live storage: a list because reports are added at the head and read as a list. A repeat
// replaces the list rather than mutating a report, so a reader can key its cache on the list's
// identity and see the new count.
let mutable private entries : Report list = []
let mutable private total = 0

/// Record a report and write it to the console, exactly as `eprintfn` did: the console line is unchanged
/// and the tally is additive (spec 081, FR-006).
let report (message: string) : unit =
    do eprintfn "%s" message
    total <- if total >= maxCount then maxCount else total + 1

    if List.exists (fun entry -> entry.Message = message) entries then
        entries <-
            entries
            |> List.map (fun entry ->
                if entry.Message = message then { entry with Count = min maxCount (entry.Count + 1) }
                else entry)
    else
        entries <- List.truncate maxEntries ({ Message = message; Count = 1 } :: entries)

/// `report` with a format string, so a call site reads as the `eprintfn` it replaces.
let reportf (format: Printf.StringFormat<'T, unit>) : 'T = Printf.kprintf report format

/// How many reports have been made this run.
let errorCount () : int = total

/// The most recent report's text, or "" when there are none.
let lastMessage () : string =
    match entries with
    | entry :: _ -> entry.Message
    | [] -> ""

/// The distinct reports, most recent first, each with its count. Returns the live storage: the
/// list is immutable, so a reader may hold it (and compare its identity) without a copy.
let recent () : Report list = entries

/// Whether the tally has crossed the escalation threshold.
let isLoud () : bool = total >= loudThreshold

/// Forget everything (a fresh session).
let clear () : unit =
    entries <- []
    total <- 0
