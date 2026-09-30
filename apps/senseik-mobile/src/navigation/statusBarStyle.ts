/**
 * B9/redesign: both the auth stack and (since the 2026-09 redesign) every app screen sit on the dark
 * brand backdrop, so the status bar content is always light. Kept as an exported pure function
 * (B18) so `RootNavigator`'s own effects, screens that re-assert the style and tests share one
 * source. Lives outside `RootNavigator` so a screen can import it without an import cycle.
 */
export function resolveStatusBarStyle(): "light" {
  return "light";
}
