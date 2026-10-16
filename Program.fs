// fsharp-game entry point. All initialisation and the frame loop live in
// Shell.run; keep this file as thin as possible.

[<EntryPoint>]
let main _argv =
    // A failure before the loop (startup) is reported and returns a failure code rather than escaping with a
    // stack trace (spec 080, US8). A failure inside the loop is caught and torn down there; this is the backstop.
    try Shell.run ()
    with ex ->
        do Diagnostics.reportf "engine failed: %s" ex.Message
        1
