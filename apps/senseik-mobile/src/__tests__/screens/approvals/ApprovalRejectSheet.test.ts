import { isRejectSubmitDisabled } from "@/screens/app/approvals/ApprovalRejectSheet";

/**
 * Kontrolcü düzeltmesi (P07 tur 1): backend `ValidateDecisionComment` (denetim A10) reddi - tekli
 * de toplu da - boş/boşluk gerekçeyle her zaman 400'ler. "Reddet" düğmesi artık ikisinde de aynı
 * tek fonksiyonla (bu dosyanın componenti içinde kullanılan `isRejectSubmitDisabled`) pasif tutulur.
 */
describe("isRejectSubmitDisabled", () => {
  it("disables submit when the reason is empty", () => {
    expect(isRejectSubmitDisabled("", false)).toBe(true);
  });

  it("disables submit when the reason is only whitespace", () => {
    expect(isRejectSubmitDisabled("   ", false)).toBe(true);
  });

  it("enables submit once a non-whitespace reason is typed", () => {
    expect(isRejectSubmitDisabled("Bütçe uygun değil", false)).toBe(false);
  });

  it("stays disabled while a decision is already in flight, even with a reason typed", () => {
    expect(isRejectSubmitDisabled("Bütçe uygun değil", true)).toBe(true);
  });
});
