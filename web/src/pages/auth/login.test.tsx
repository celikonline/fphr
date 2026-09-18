import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter } from "react-router";
import trAuth from "../../../public/locales/tr/auth.json";
import trCommon from "../../../public/locales/tr/common.json";
import LoginPage from "./login";
import { useAuthStore, type TenantChoice } from "@/store/auth.store";

// senseik-025: LoginPage only reads `login` / `completeMfaLogin` off the store via selectors, so a bare mock
// implementing the selector call is enough — no need to spin up the real zustand store or hit the network.
vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

const navigateMock = vi.fn();
vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => navigateMock };
});

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Modal/Menu overlays need one of these
// mounted to render without throwing (same setup as leave-type-dialog.test.tsx).
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
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "auth",
    ns: ["auth", "common"],
    resources: { tr: { auth: trAuth, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

const TENANTS: TenantChoice[] = [
  { tenantId: "t-1", slug: "acme", name: "Acme Yazılım A.Ş." },
  { tenantId: "t-2", slug: "beta-holding", name: "Beta Holding" },
];

function renderLogin(login: ReturnType<typeof vi.fn>) {
  mockUseAuthStore.mockImplementation((selector) => {
    const state = { login, completeMfaLogin: vi.fn() };
    return typeof selector === "function" ? selector(state) : state;
  });

  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>
      </MantineProvider>
    </I18nextProvider>,
  );
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>, email: string, password: string) {
  const emailInput = screen.getByLabelText(trAuth.email);
  const passwordInput = screen.getByLabelText(trAuth.password);
  await user.clear(emailInput);
  await user.type(emailInput, email);
  await user.clear(passwordInput);
  await user.type(passwordInput, password);
  await user.click(screen.getByRole("button", { name: trAuth.login }));
}

describe("LoginPage tenant select popup (senseik-025)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("opens a card/avatar row per tenant when login resolves select_tenant, and closes via the X button", async () => {
    const user = userEvent.setup();
    const login = vi.fn().mockResolvedValue({ status: "select_tenant", tenants: TENANTS });
    renderLogin(login);

    await fillAndSubmit(user, "multi@demo.local", "secret123");

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(trAuth.selectCompany.title)).not.toBeNull();
    for (const tenant of TENANTS) {
      expect(within(dialog).getByRole("button", { name: new RegExp(tenant.name) })).not.toBeNull();
      expect(within(dialog).getByText(tenant.slug)).not.toBeNull();
    }

    await user.click(screen.getByRole("button", { name: trCommon.close }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("closes via Escape and lets the user retry with a different email without the form staying disabled", async () => {
    const user = userEvent.setup();
    const login = vi
      .fn()
      .mockResolvedValueOnce({ status: "select_tenant", tenants: TENANTS })
      .mockResolvedValueOnce({ status: "ok" });
    renderLogin(login);

    await fillAndSubmit(user, "multi@demo.local", "secret123");
    await screen.findByRole("dialog");

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    // Not locked: the submit button and both inputs are interactive again right after the popup closes.
    const emailInput = screen.getByLabelText(trAuth.email) as HTMLInputElement;
    const passwordInput = screen.getByLabelText(trAuth.password) as HTMLInputElement;
    const submitButton = screen.getByRole("button", { name: trAuth.login }) as HTMLButtonElement;
    expect(emailInput.disabled).toBe(false);
    expect(passwordInput.disabled).toBe(false);
    expect(submitButton.disabled).toBe(false);

    // The email typed before the popup opened is still there — closing doesn't wipe form state.
    expect(emailInput.value).toBe("multi@demo.local");

    await user.clear(emailInput);
    await user.type(emailInput, "other@demo.local");
    await user.click(screen.getByRole("button", { name: trAuth.login }));

    await waitFor(() => expect(login).toHaveBeenCalledTimes(2));
    expect(login).toHaveBeenNthCalledWith(2, "other@demo.local", "secret123");
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith("/app", { replace: true }));
  });

  it("selects a tenant from the redesigned card list and completes login with its slug", async () => {
    const user = userEvent.setup();
    const login = vi
      .fn()
      .mockResolvedValueOnce({ status: "select_tenant", tenants: TENANTS })
      .mockResolvedValueOnce({ status: "ok" });
    renderLogin(login);

    await fillAndSubmit(user, "multi@demo.local", "secret123");
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: new RegExp(TENANTS[1].name) }));

    await waitFor(() => expect(login).toHaveBeenCalledTimes(2));
    expect(login).toHaveBeenNthCalledWith(2, "multi@demo.local", "secret123", TENANTS[1].slug);
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith("/app", { replace: true }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });
});
