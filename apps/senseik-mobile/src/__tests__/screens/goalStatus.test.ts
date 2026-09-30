import { goalStatusTone } from "@/screens/app/goals/goalStatus";

describe("goalStatusTone (mobil yeniden tasarım Task 6)", () => {
  it("maps completed to the success tone", () => {
    expect(goalStatusTone("completed")).toBe("success");
  });

  it("maps inProgress to the info tone", () => {
    expect(goalStatusTone("inProgress")).toBe("info");
  });

  it("maps cancelled to the danger tone", () => {
    expect(goalStatusTone("cancelled")).toBe("danger");
  });

  it("maps open and archived to the neutral tone", () => {
    expect(goalStatusTone("open")).toBe("neutral");
    expect(goalStatusTone("archived")).toBe("neutral");
  });
});
