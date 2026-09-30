import { formatUnreadBadge } from "@/screens/app/DashboardScreen";
import {
  balanceFraction,
  birthdayCountdown,
  plainTextPreview,
  sectionFrom,
} from "@/screens/app/home/homeSections";

// DashboardScreen's top bar renders BrandMark; react-native-reanimated v4's real module throws
// outside a native runtime (same stub as RootNavigator.test.tsx / LoginScreen.test.tsx).
jest.mock("react-native-reanimated", () => {
  const { View, Text } = jest.requireActual("react-native");
  return {
    __esModule: true,
    default: { View, Text },
    useSharedValue: (initial: unknown) => ({ value: initial }),
    useAnimatedStyle: (factory: () => Record<string, unknown>) => factory(),
    useReducedMotion: () => false,
    withSpring: (toValue: unknown) => toValue,
    withTiming: (toValue: unknown) => toValue,
    withDelay: (_delay: number, animation: unknown) => animation,
  };
});

describe("formatUnreadBadge (N1)", () => {
  it("shows the exact count up to 9", () => {
    expect(formatUnreadBadge(0)).toBe("0");
    expect(formatUnreadBadge(1)).toBe("1");
    expect(formatUnreadBadge(9)).toBe("9");
  });

  it("caps the badge at '9+' beyond 9", () => {
    expect(formatUnreadBadge(10)).toBe("9+");
    expect(formatUnreadBadge(42)).toBe("9+");
  });
});

describe("sectionFrom", () => {
  it("hides a section whose request failed or came back empty", () => {
    expect(sectionFrom({ status: "rejected", reason: new Error("403") }, (x) => x)).toBeNull();
    expect(sectionFrom({ status: "fulfilled", value: [] }, (x) => x)).toBeNull();
    expect(sectionFrom({ status: "fulfilled", value: [1, 2] }, (x) => x)).toEqual([1, 2]);
  });

  it("hides a section whose selector filtered everything out", () => {
    expect(sectionFrom({ status: "fulfilled", value: [1, 2] }, () => [])).toBeNull();
  });
});

describe("balanceFraction", () => {
  const zero = {
    carriedOverIntoPeriod: 0,
    lastPeriodAccrued: 0,
    extraGrants: 0,
    adjustments: 0,
    availableBalance: 0,
  };

  it("uses the period entitlement (carried over + accrued + extra + adjustments) as the total", () => {
    expect(
      balanceFraction({
        ...zero,
        carriedOverIntoPeriod: 30,
        lastPeriodAccrued: 14,
        extraGrants: 1,
        adjustments: -1,
        availableBalance: 40.3,
      })
    ).toEqual({ available: 40.3, entitlement: 44, progress: 40.3 / 44 });
  });

  it("has no total when the period grants nothing (only 'n gün kullanılabilir' is shown)", () => {
    expect(balanceFraction({ ...zero, availableBalance: 3 })).toEqual({
      available: 3,
      entitlement: null,
      progress: 0,
    });
  });

  it("clamps the progress bar between empty and full", () => {
    expect(balanceFraction({ ...zero, lastPeriodAccrued: 5, availableBalance: -2 }).progress).toBe(
      0
    );
    expect(balanceFraction({ ...zero, lastPeriodAccrued: 5, availableBalance: 7 }).progress).toBe(
      1
    );
  });
});

describe("birthdayCountdown", () => {
  it("says 'Bugün' on the day and counts down otherwise", () => {
    expect(birthdayCountdown(0)).toBe("Bugün");
    expect(birthdayCountdown(5)).toBe("5 gün kaldı");
  });
});

describe("plainTextPreview", () => {
  it("strips the rich-text markup announcements are stored with", () => {
    expect(
      plainTextPreview(
        "<p>İzin&nbsp;talepleri <strong>artık</strong></p><p>tek dokunuşla &amp; hızlı.</p>"
      )
    ).toBe("İzin talepleri artık tek dokunuşla & hızlı.");
  });

  it("leaves plain text untouched", () => {
    expect(plainTextPreview("Sade metin")).toBe("Sade metin");
  });
});
