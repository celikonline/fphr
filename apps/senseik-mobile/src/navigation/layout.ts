/** Height of the custom tab bar (see the ana sayfa mockup's `.tabbar`), excluding the bottom safe
 * area - shared by `Screen`'s default bottom inset and `NotchedTabBar` itself. */
export const TAB_BAR_HEIGHT = 72;

/** Outer size of the tab bar's centre FAB (two overlapping 54x54 squares, mockup `.fab`). */
export const FAB_SIZE = 62;

/** Bottom padding for scrollable screen content so the last item never sits under the tab bar. */
export function contentBottomInset(safeBottom: number): number {
  return TAB_BAR_HEIGHT + safeBottom + 16;
}

/** Mockup width the notch geometry below was drawn at (`viewBox="0 0 390 92"`). */
const MOCKUP_WIDTH = 390;
const CORNER_RADIUS = 24;

/** Rounds to at most two decimals and drops trailing zeros ("195", "196.5"). */
function fmt(value: number): string {
  return String(Math.round(value * 100) / 100);
}

/**
 * The tab bar background: the mockup's path (drawn for 390 wide) with its notch kept at its drawn
 * size and re-centred on `width / 2`, so the notch always fits the fixed-size FAB; only the flat
 * segments either side stretch. At 390x92 it reproduces the mockup path verbatim.
 */
export function buildNotchPath(width: number, height: number): string {
  // Shifts a mockup x coordinate so the mockup's centre (195) lands on this bar's centre.
  const rel = (mockupX: number) => fmt(mockupX + (width - MOCKUP_WIDTH) / 2);

  return [
    `M0 ${CORNER_RADIUS} Q0 0 ${CORNER_RADIUS} 0`,
    `H${rel(136)}`,
    `C${rel(148)} 0 ${rel(152)} 3 ${rel(155)} 12`,
    `C${rel(161)} 34 ${rel(174)} 50 ${rel(195)} 50`,
    `C${rel(216)} 50 ${rel(229)} 34 ${rel(235)} 12`,
    `C${rel(238)} 3 ${rel(242)} 0 ${rel(254)} 0`,
    `H${fmt(width - CORNER_RADIUS)} Q${fmt(width)} 0 ${fmt(width)} ${CORNER_RADIUS}`,
    `V${fmt(height)} H0 Z`,
  ].join(" ");
}
