import { shouldRetryOnForeground } from "@/screens/app/checkin/locationRetry";

describe("shouldRetryOnForeground", () => {
  it("re-checks after 'denied': the user may be back from granting it in the system settings", () => {
    expect(shouldRetryOnForeground("denied")).toBe(true);
  });

  it("does not restart after 'unavailable': that would reopen the declined accuracy dialog in a loop", () => {
    expect(shouldRetryOnForeground("unavailable")).toBe(false);
  });

  it("re-checks after 'servicesOff': the user may be back from turning location services on", () => {
    expect(shouldRetryOnForeground("servicesOff")).toBe(true);
  });

  it("leaves a running watch alone once a fix is 'ready'", () => {
    expect(shouldRetryOnForeground("ready")).toBe(false);
  });

  it("does not start a second attempt while the first is still 'pending'", () => {
    expect(shouldRetryOnForeground("pending")).toBe(false);
  });
});
