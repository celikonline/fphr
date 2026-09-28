import { leaveRequestBlocker } from "@/components/leave/LeaveRequestForm";

const annual = {
  unit: "fullDay" as const,
  requiresDescription: false,
  requiresDocument: false,
  requiresSubstitute: false,
};

const valid = {
  selectedType: annual,
  startDate: "2026-10-01",
  endDate: "2026-10-03",
  hourlyDuration: null,
  description: "",
  hasSubstitute: false,
  documentCount: 0,
  previewBlocksSubmit: false,
};

describe("leaveRequestBlocker (submit-time checks, same order and messages as before)", () => {
  it("returns null for a complete full-day request", () => {
    expect(leaveRequestBlocker(valid)).toBeNull();
  });

  it("asks for a leave type first", () => {
    expect(leaveRequestBlocker({ ...valid, selectedType: null })).toEqual({
      title: "Eksik bilgi",
      message: "Lütfen bir izin türü seçin.",
    });
  });

  it("asks for a date", () => {
    expect(leaveRequestBlocker({ ...valid, startDate: "" })?.message).toBe(
      "Lütfen bir tarih girin."
    );
  });

  it("asks for both dates on a full-day request without an end date", () => {
    expect(leaveRequestBlocker({ ...valid, endDate: "" })?.message).toBe(
      "Lütfen başlangıç ve bitiş tarihini girin."
    );
  });

  it("rejects an end date before the start date", () => {
    expect(leaveRequestBlocker({ ...valid, endDate: "2026-09-30" })).toEqual({
      title: "Geçersiz tarih",
      message: "Bitiş tarihi başlangıç tarihinden önce olamaz.",
    });
  });

  it("ignores the end date on a half-day request", () => {
    const halfDay = { ...annual, unit: "halfDay" as const };
    expect(leaveRequestBlocker({ ...valid, selectedType: halfDay, endDate: "" })).toBeNull();
  });

  it("rejects an hourly request without a positive duration", () => {
    const hourly = { ...annual, unit: "hourly" as const };
    expect(leaveRequestBlocker({ ...valid, selectedType: hourly })?.title).toBe("Geçersiz saat");
    expect(
      leaveRequestBlocker({ ...valid, selectedType: hourly, hourlyDuration: "2 sa 0 dk" })
    ).toBeNull();
  });

  it("requires a description when the type does (whitespace does not count)", () => {
    const type = { ...annual, requiresDescription: true };
    expect(leaveRequestBlocker({ ...valid, selectedType: type, description: "  " })?.message).toBe(
      "Bu izin türü için açıklama zorunludur."
    );
  });

  it("requires a substitute when the type does", () => {
    const type = { ...annual, requiresSubstitute: true };
    expect(leaveRequestBlocker({ ...valid, selectedType: type })?.message).toBe(
      "Bu izin türü için bir vekil seçmeniz gerekir."
    );
    expect(leaveRequestBlocker({ ...valid, selectedType: type, hasSubstitute: true })).toBeNull();
  });

  it("requires a document when the type does", () => {
    const type = { ...annual, requiresDocument: true };
    expect(leaveRequestBlocker({ ...valid, selectedType: type })?.message).toBe(
      "Bu izin türü için en az bir belge eklemeniz gerekir."
    );
    expect(leaveRequestBlocker({ ...valid, selectedType: type, documentCount: 1 })).toBeNull();
  });

  it("blocks a request the preview says deducts no days", () => {
    expect(leaveRequestBlocker({ ...valid, previewBlocksSubmit: true })).toEqual({
      title: "Gün düşülmeyecek",
      message: "Bu tarihlerde izninizden düşülecek gün yok.",
    });
  });
});
