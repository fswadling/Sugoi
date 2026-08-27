# Quickstart: Screens + Main Menu

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Native ImGui library built (`./native/build.sh`, already done).

## Build & run

```sh
dotnet build
dotnet run
```

## Validation scenarios

1. **Launch to menu** — run; expect a main menu window with "Play" and "Quit".
2. **Play** — select Play; expect the gameplay placeholder screen.
3. **Quit** — select Quit (or close the window); expect a clean exit (no crash,
   "clean shutdown" logged).

## Expected outcome

- Menu appears on launch (US1).
- Play navigates to gameplay (US2).
- Quit/close exits cleanly (US3).
