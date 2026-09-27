/** Height of the custom tab bar (see the ana sayfa mockup's `.tabbar`) - shared by `Screen`'s
 * default bottom inset and (Task 3) the tab bar component itself. */
export const TAB_BAR_HEIGHT = 72;

/** Bottom padding for scrollable screen content so the last item never sits under the tab bar. */
export function contentBottomInset(safeBottom: number): number {
  return TAB_BAR_HEIGHT + safeBottom + 16;
}
