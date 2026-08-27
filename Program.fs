// fsharp-game entry point. All initialisation and the frame loop live in
// Engine.run; keep this file as thin as possible.

[<EntryPoint>]
let main _argv =
    Engine.run ()
