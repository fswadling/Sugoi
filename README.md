# fsharp-game

A game written in F# using SDL3, ImGui, and Jolt Physics, structured after `odin-game`.

## Prerequisites

- .NET 10 SDK
- SDL3 native library (for the `SDL3-CS` bindings)
- `cimgui` native library (for the `ImGui.NET` bindings)

## Build

```sh
dotnet build
```

## Run

```sh
dotnet run
```

## Spec-Driven Development

This project uses Spec Kit ("speckit"). Features are driven from specs under
`specs/` and go through specify → plan → tasks → implement → converge.
