import {
  homeFeedService,
  selectPeopleOffToday,
  selectUpcomingBirthdays,
  selectUpcomingEvents,
  selectAnnouncements,
  sortBalancesAnnualFirst,
} from "@/services/HomeFeedService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

const base = { calculatedDays: 5, startDate: "2026-09-29", endDate: "2026-10-03", isMasked: false };

describe("selectPeopleOffToday", () => {
  it("keeps approved others, masks hidden types, flags remote work", () => {
    const out = selectPeopleOffToday(
      [
        {
          ...base,
          requestId: "1",
          employeeId: "me",
          employeeName: "Ali Şahin",
          status: "approved",
          leaveTypeCode: "annual",
          leaveTypeName: "Yıllık izin",
        },
        {
          ...base,
          requestId: "2",
          employeeId: "a",
          employeeName: "Selin Aydın",
          status: "approved",
          leaveTypeCode: "annual",
          leaveTypeName: "Yıllık izin",
        },
        {
          ...base,
          requestId: "3",
          employeeId: "b",
          employeeName: "Murat Kaya",
          status: "pending",
          leaveTypeCode: "annual",
          leaveTypeName: "Yıllık izin",
        },
        {
          ...base,
          requestId: "4",
          employeeId: "c",
          employeeName: "Ece Demir",
          status: "approved",
          isMasked: true,
        },
        {
          ...base,
          requestId: "5",
          employeeId: "d",
          employeeName: "Can Uz",
          status: "approved",
          leaveTypeCode: "remoteWork",
          leaveTypeName: "Uzaktan çalışma",
        },
      ],
      "me"
    );
    expect(out.map((p) => p.employeeId)).toEqual(["a", "c", "d"]);
    expect(out[1].typeLabel).toBe("İzinli");
    expect(out[2]).toMatchObject({ isRemote: true, typeLabel: "Uzaktan çalışıyor" });
  });

  it("uses the leave type name, falls back to 'İzinli', and carries the leave's days and dates", () => {
    const [person] = selectPeopleOffToday(
      [
        {
          ...base,
          requestId: "1",
          employeeId: "a",
          employeeName: "Selin Aydın",
          status: "approved",
        },
      ],
      undefined
    );
    expect(person).toEqual({
      employeeId: "a",
      name: "Selin Aydın",
      typeLabel: "İzinli",
      days: 5,
      startDate: "2026-09-29",
      endDate: "2026-10-03",
      isRemote: false,
    });
  });

  it("lists each person once and caps the list at 5", () => {
    const entries = ["a", "a", "b", "c", "d", "e", "f"].map((employeeId, index) => ({
      ...base,
      requestId: String(index),
      employeeId,
      employeeName: employeeId.toUpperCase(),
      status: "approved" as const,
    }));
    expect(selectPeopleOffToday(entries, "me").map((p) => p.employeeId)).toEqual([
      "a",
      "b",
      "c",
      "d",
      "e",
    ]);
  });
});

describe("selectUpcomingBirthdays", () => {
  it("counts days left to birthdays and caps the list", () => {
    const out = selectUpcomingBirthdays(
      [
        { id: "1", displayName: "Ece Demir", nextBirthday: "2026-10-02" },
        { id: "2", displayName: "Can Uz", nextBirthday: "2026-09-27" },
        { id: "3", displayName: "X", nextBirthday: "2026-10-10" },
        { id: "4", displayName: "Y", nextBirthday: "2026-10-11" },
      ],
      "2026-09-27"
    );
    expect(out).toHaveLength(3);
    expect(out[0]).toMatchObject({ id: "2", daysLeft: 0 });
    expect(out[1]).toMatchObject({ id: "1", daysLeft: 5 });
  });

  it("counts whole calendar days across a month/year boundary", () => {
    const [next] = selectUpcomingBirthdays(
      [{ id: "1", displayName: "Ece Demir", nextBirthday: "2027-01-02" }],
      "2026-12-30"
    );
    expect(next.daysLeft).toBe(3);
  });
});

describe("selectUpcomingEvents", () => {
  it("keeps only company events and meetings", () => {
    expect(
      selectUpcomingEvents([
        { title: "Doğum günü", date: "2026-10-02", type: "birthday" },
        { title: "İSG eğitimi", date: "2026-10-08", type: "companyEvent" },
        { title: "Aylık toplantı", date: "2026-10-09", type: "meeting" },
      ]).map((e) => e.title)
    ).toEqual(["İSG eğitimi", "Aylık toplantı"]);
  });

  it("orders by date and caps the list at 3", () => {
    const events = ["2026-10-20", "2026-10-01", "2026-10-15", "2026-10-05"].map((date) => ({
      title: date,
      date,
      type: "meeting" as const,
    }));
    expect(selectUpcomingEvents(events).map((e) => e.date)).toEqual([
      "2026-10-01",
      "2026-10-05",
      "2026-10-15",
    ]);
  });
});

describe("selectAnnouncements", () => {
  it("keeps announcements regardless of casing and drops recognitions", () => {
    const a = { content: "", type: "General", priority: "Normal", isRead: false };
    expect(
      selectAnnouncements([
        { ...a, id: "1", title: "Eski", kind: "Announcement", publishedAt: "2026-09-01T08:00:00Z" },
        {
          ...a,
          id: "2",
          title: "Tebrik",
          kind: "Recognition",
          publishedAt: "2026-09-26T08:00:00Z",
        },
        { ...a, id: "3", title: "Yeni", kind: "announcement", publishedAt: "2026-09-25T08:00:00Z" },
      ]).map((x) => x.id)
    ).toEqual(["3", "1"]);
  });

  it("caps the list at 2", () => {
    const a = {
      content: "",
      type: "General",
      priority: "Normal",
      isRead: true,
      kind: "announcement",
    };
    expect(
      selectAnnouncements([
        { ...a, id: "1", title: "1", publishedAt: "2026-09-01T08:00:00Z" },
        { ...a, id: "2", title: "2", publishedAt: "2026-09-03T08:00:00Z" },
        { ...a, id: "3", title: "3", publishedAt: "2026-09-02T08:00:00Z" },
      ]).map((x) => x.id)
    ).toEqual(["2", "3"]);
  });
});

describe("sortBalancesAnnualFirst", () => {
  it("puts annual leave first", () => {
    expect(
      sortBalancesAnnualFirst([{ leaveTypeCode: "excuse" }, { leaveTypeCode: "annual" }])[0]
        .leaveTypeCode
    ).toBe("annual");
  });

  it("keeps the others in their original order without mutating the input", () => {
    const input = [
      { leaveTypeCode: "excuse" },
      { leaveTypeCode: "sick" },
      { leaveTypeCode: "annual" },
    ];
    expect(sortBalancesAnnualFirst(input).map((b) => b.leaveTypeCode)).toEqual([
      "annual",
      "excuse",
      "sick",
    ]);
    expect(input[0].leaveTypeCode).toBe("excuse");
  });
});

describe("HomeFeedService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (apiClient.get as jest.Mock).mockResolvedValue({ data: [] });
  });

  it("reads the leave calendar for the given range", async () => {
    await homeFeedService.getLeaveCalendar("2026-09-27", "2026-09-27");
    expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/calendar", {
      params: { from: "2026-09-27", to: "2026-09-27" },
    });
  });

  it("reads upcoming birthdays for 30 days by default", async () => {
    await homeFeedService.getUpcomingBirthdays();
    expect(apiClient.get).toHaveBeenCalledWith("/api/v1/employees/upcoming-birthdays", {
      params: { days: 30 },
    });
  });

  it("reads the company calendar for the given range", async () => {
    await homeFeedService.getCompanyCalendar("2026-09-27", "2026-10-27");
    expect(apiClient.get).toHaveBeenCalledWith("/api/v1/announcements/events/calendar", {
      params: { from: "2026-09-27", to: "2026-10-27" },
    });
  });

  it("reads the caller's announcement feed", async () => {
    const feed = [{ id: "1", title: "Duyuru", kind: "announcement" }];
    (apiClient.get as jest.Mock).mockResolvedValue({ data: feed });
    await expect(homeFeedService.getAnnouncementFeed()).resolves.toEqual(feed);
    expect(apiClient.get).toHaveBeenCalledWith("/api/v1/announcements/feed");
  });
});
