module Screen

// Screen kinds and navigation requests shared by all screens. New screens are
// added as `ScreenKind` cases here and handled in `ScreenManager`.

type ScreenKind =
    | MainMenu
    | Gameplay

// A navigation request produced by a screen and applied by the manager at the
// end of the frame (so the current frame still draws fully).
type ScreenTransition =
    | None
    | Next of ScreenKind
    | Quit
