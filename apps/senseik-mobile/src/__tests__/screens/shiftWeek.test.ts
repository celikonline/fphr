import {
  emptyShiftTexts,
  formatDayLabel,
  formatWeekLabel,
  weekDays,
} from "@/screens/app/shift/shiftWeek";

describe("weekDays", () => {
  it("lists Monday..Sunday of the week containing a midweek day", () => {
    // 30 Eylül 2026 Çarşamba
    expect(weekDays(new Date(2026, 8, 30))).toEqual([
      "2026-09-28",
      "2026-09-29",
      "2026-09-30",
      "2026-10-01",
      "2026-10-02",
      "2026-10-03",
      "2026-10-04",
    ]);
  });

  it("treats Sunday as the last day of its week", () => {
    const days = weekDays(new Date(2026, 9, 4)); // 4 Ekim 2026 Pazar
    expect(days[0]).toBe("2026-09-28");
    expect(days[6]).toBe("2026-10-04");
  });

  it("crosses a year boundary", () => {
    expect(weekDays(new Date(2027, 0, 1))[0]).toBe("2026-12-28");
  });
});

describe("week labels", () => {
  it("formats the week span", () => {
    expect(formatWeekLabel(["2026-09-28", "2026-10-04"])).toBe("28 Eyl – 4 Eki");
  });

  it("formats a day with its Turkish weekday", () => {
    expect(formatDayLabel("2026-09-30")).toBe("Çar, 30 Eyl");
  });
});

describe("emptyShiftTexts", () => {
  it("speaks of the week when no day is selected", () => {
    expect(emptyShiftTexts(null)).toEqual({
      mine: "Bu hafta için yayınlanmış vardiyanız yok.",
      team: "Bu hafta için görüntüleyebileceğiniz ekip vardiyası yok.",
    });
  });

  it("speaks of the day for both lists once a day is selected", () => {
    expect(emptyShiftTexts("2026-09-30")).toEqual({
      mine: "Bu gün için vardiyanız yok.",
      team: "Bu gün için görüntüleyebileceğiniz ekip vardiyası yok.",
    });
  });
});
