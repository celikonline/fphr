import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAccount from "../../public/locales/tr/account.json";
import trAuth from "../../public/locales/tr/auth.json";
import trCommon from "../../public/locales/tr/common.json";
import trNavigation from "../../public/locales/tr/navigation.json";
import AccountPage from "./account";
import type { MfaStatus } from "@/services/mfa.service";

// FG-022: `GET me/mfa` -> `requiredByPolicy` - the server refuses to turn MFA off for a policy-required role
// (`identity.mfa.required_by_policy`), so the security card disables its "turn off" button and says why.
const getMfaStatus = vi.fn<() => Promise<MfaStatus>>();

vi.mock("@/services/mfa.service", async () => {
  const actual =
    await vi.importActual<typeof import("@/services/mfa.service")>("@/services/mfa.service");
  return { ...actual, getMfaStatus: () => getMfaStatus() };
});

vi.mock("@/store/auth.store", () => {
  const state = {
    user: {
      id: "user-1",
      email: "ayse@ornek.com",
      name: "Ayşe Yılmaz",
      roles: [],
      roleLabels: [],
      permissions: [],
      mfaEnabled: true,
    },
    logout: vi.fn(),
    refreshMe: vi.fn(),
  };
  return { useAuthStore: (selector: (s: typeof state) => unknown) => selector(state) };
});

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "account",
    ns: ["account", "auth", "common", "navigation"],
    resources: {
      tr: { account: trAccount, auth: trAuth, common: trCommon, navigation: trNavigation },
    },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <AccountPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

const disableLabel = trAccount.mfaSection.disableCta;
const policyHint = trAccount.mfaRequiredByPolicyHint;

describe("AccountPage MFA card (FG-022)", () => {
  beforeEach(() => {
    getMfaStatus.mockReset();
  });

  it("disables the turn-off button and explains why when the policy requires MFA", async () => {
    getMfaStatus.mockResolvedValue({
      enabled: true,
      pendingEnrollment: false,
      requiredByPolicy: true,
    });

    renderPage();

    const hint = await screen.findByText(policyHint);
    const button = screen.getByRole("button", { name: disableLabel });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("aria-describedby", hint.id);
  });

  it("keeps the turn-off button active and opens the dialog when the policy does not require MFA", async () => {
    const user = userEvent.setup();
    getMfaStatus.mockResolvedValue({
      enabled: true,
      pendingEnrollment: false,
      requiredByPolicy: false,
    });

    renderPage();

    const button = await screen.findByRole("button", { name: disableLabel });
    await vi.waitFor(() => expect(getMfaStatus).toHaveBeenCalled());
    expect(button).toBeEnabled();
    expect(screen.queryByText(policyHint)).not.toBeInTheDocument();

    await user.click(button);
    expect(await screen.findByText(trAccount.mfaSection.disableHint)).toBeInTheDocument();
  });

  it("treats a missing requiredByPolicy (older API) as not required", async () => {
    getMfaStatus.mockResolvedValue({ enabled: true, pendingEnrollment: false });

    renderPage();

    const button = await screen.findByRole("button", { name: disableLabel });
    await vi.waitFor(() => expect(getMfaStatus).toHaveBeenCalled());
    expect(button).toBeEnabled();
    expect(screen.queryByText(policyHint)).not.toBeInTheDocument();
  });
});
