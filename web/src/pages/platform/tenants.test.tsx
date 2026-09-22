import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { AxiosError } from "axios";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPlatform from "../../../public/locales/tr/platform.json";
import trCommon from "../../../public/locales/tr/common.json";
import trSetup from "../../../public/locales/tr/setup.json";
import PlatformTenantsPage from "./tenants";
import { createTenant, listTenants, updateTenant, type TenantSummary } from "@/services/platform.service";
import { toast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/auth.store";

vi.mock("@/services/platform.service", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/platform.service")>()),
  listTenants: vi.fn(),
  createTenant: vi.fn(),
  updateTenant: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

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

const TENANTS: TenantSummary[] = [
  {
    id: "t2",
    slug: "acme",
    name: "Acme A.Ş.",
    status: "trial",
    createdAt: "2026-09-20T10:00:00Z",
    isPlatform: false,
    taxNumber: "1234567890",
    locale: "tr-TR",
    timeZone: "Europe/Istanbul",
  },
  {
    id: "t1",
    slug: "platform",
    name: "Platform",
    status: "active",
    createdAt: "2026-09-01T10:00:00Z",
    isPlatform: true,
    locale: "tr-TR",
    timeZone: "Europe/Istanbul",
  },
];

function setPermissions(permissions: string[]) {
  useAuthStore.setState({
    token: null,
    user: {
      id: "u1",
      email: "root@sense.test",
      name: "Root",
      roles: [],
      roleLabels: [],
      permissions,
    },
    features: {
      access: {} as never,
      accessSettingsBypass: true,
      mfaSetupRequired: false,
      disabledModules: [],
    },
  });
}

function renderPage() {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    ns: ["platform", "common", "setup"],
    resources: { tr: { platform: trPlatform, common: trCommon, setup: trSetup } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <PlatformTenantsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

function slugTakenError(): AxiosError {
  return new AxiosError("conflict", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 409,
    statusText: "Conflict",
    headers: {},
    config: {} as never,
    data: { code: "tenant.slug_taken", detail: "Bu şirket adresi zaten kullanımda." },
  });
}

async function fillCommonFields(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: trPlatform.add }));
  await user.type(await screen.findByLabelText(/^Şirket adı/), "Örnek Teknoloji A.Ş.");
  await user.type(screen.getByLabelText(/^Ad\s*\*?$/), "Ali");
  await user.type(screen.getByLabelText(/^Soyad/), "Veli");
  await user.type(screen.getByLabelText(/^E-posta/), "boss@ornek.test");
}

describe("PlatformTenantsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listTenants).mockResolvedValue(TENANTS);
    vi.mocked(createTenant).mockResolvedValue({
      tenantId: "t3",
      slug: "ornek",
      ownerUserId: "u3",
      invitationSent: true,
    });
  });

  it("shows the no-access page without the platform permission and never loads tenants", () => {
    setPermissions(["identity.tenant.manage", "identity.users.manage"]);
    renderPage();

    expect(screen.getByText(trCommon.noAccessTitle)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: trPlatform.add })).not.toBeInTheDocument();
    expect(listTenants).not.toHaveBeenCalled();
  });

  it("lists tenants with status and flags the platform tenant", async () => {
    setPermissions(["platform.tenants.manage"]);
    renderPage();

    expect((await screen.findAllByText("Acme A.Ş.")).length).toBeGreaterThan(0);
    expect(screen.getAllByText(trPlatform.status.trial).length).toBeGreaterThan(0);
    expect(screen.getAllByText(trPlatform.platformBadge).length).toBeGreaterThan(0);
  });

  it("creates a tenant in invite mode with the slug suggested from the company name", async () => {
    setPermissions(["platform.tenants.manage"]);
    const user = userEvent.setup();
    renderPage();

    await fillCommonFields(user);
    expect(screen.getByLabelText(/^Şirket adresi/)).toHaveValue("ornek-teknoloji-a-s");

    await user.click(screen.getByRole("button", { name: trPlatform.dialog.submit }));

    await waitFor(() => expect(createTenant).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createTenant).mock.calls[0][0]).toEqual({
      companyName: "Örnek Teknoloji A.Ş.",
      slug: "ornek-teknoloji-a-s",
      ownerEmail: "boss@ornek.test",
      ownerFirstName: "Ali",
      ownerLastName: "Veli",
      ownerMode: "invite",
      password: undefined,
      phone: undefined,
    });
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ description: trPlatform.created.invited })
      )
    );
  });

  it("requires a long enough password in password mode and sends it once valid", async () => {
    setPermissions(["platform.tenants.manage"]);
    vi.mocked(createTenant).mockResolvedValue({
      tenantId: "t3",
      slug: "ornek",
      ownerUserId: "u3",
      invitationSent: false,
    });
    const user = userEvent.setup();
    renderPage();

    await fillCommonFields(user);
    await user.click(screen.getByText(trPlatform.dialog.modePassword));

    const passwordInput = await screen.findByLabelText(/^Parola\s*\*?$/);
    await user.type(passwordInput, "kisa");
    await user.click(screen.getByRole("button", { name: trPlatform.dialog.submit }));

    expect(await screen.findByRole("alert")).toHaveTextContent(trPlatform.errors.passwordTooShort);
    expect(createTenant).not.toHaveBeenCalled();

    await user.clear(passwordInput);
    await user.type(passwordInput, "Gunes-Deniz-2026");
    await user.click(screen.getByRole("button", { name: trPlatform.dialog.submit }));

    await waitFor(() => expect(createTenant).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createTenant).mock.calls[0][0]).toMatchObject({
      ownerMode: "password",
      password: "Gunes-Deniz-2026",
    });
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ description: trPlatform.created.withPassword })
      )
    );
  });

  it("rejects an invalid slug before calling the API", async () => {
    setPermissions(["platform.tenants.manage"]);
    const user = userEvent.setup();
    renderPage();

    await fillCommonFields(user);
    const slugInput = screen.getByLabelText(/^Şirket adresi/);
    await user.clear(slugInput);
    await user.type(slugInput, "ab");
    await user.click(screen.getByRole("button", { name: trPlatform.dialog.submit }));

    expect(await screen.findByRole("alert")).toHaveTextContent(trPlatform.errors.slugInvalid);
    expect(createTenant).not.toHaveBeenCalled();
  });

  it("maps the slug-taken problem code to a friendly message and keeps the dialog open", async () => {
    setPermissions(["platform.tenants.manage"]);
    vi.mocked(createTenant).mockRejectedValue(slugTakenError());
    const user = userEvent.setup();
    renderPage();

    await fillCommonFields(user);
    await user.click(screen.getByRole("button", { name: trPlatform.dialog.submit }));

    expect(await screen.findByRole("alert")).toHaveTextContent(trPlatform.errors.slugTaken);
    expect(screen.getByRole("button", { name: trPlatform.dialog.submit })).toBeInTheDocument();
  });

  it("edits a company: prefills the form, saves the changes and never sends the slug", async () => {
    setPermissions(["platform.tenants.manage"]);
    vi.mocked(updateTenant).mockResolvedValue({ ...TENANTS[0], name: "Acme Yazılım A.Ş." });
    const user = userEvent.setup();
    renderPage();

    await screen.findAllByText("Acme A.Ş.");
    await user.click(screen.getAllByRole("button", { name: trPlatform.edit })[0]);

    const nameInput = await screen.findByLabelText(/^Şirket adı/);
    expect(nameInput).toHaveValue("Acme A.Ş.");
    expect(screen.getByLabelText(trPlatform.editDialog.taxNumber)).toHaveValue("1234567890");

    await user.clear(nameInput);
    await user.type(nameInput, "Acme Yazılım A.Ş.");
    await user.click(screen.getByRole("button", { name: trCommon.save }));

    await waitFor(() => expect(updateTenant).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updateTenant).mock.calls[0]).toEqual([
      "t2",
      { name: "Acme Yazılım A.Ş.", taxNumber: "1234567890", locale: "tr-TR", timeZone: "Europe/Istanbul" },
    ]);
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: trPlatform.updated }))
    );
  });

  it("does not call the API when the company name is cleared", async () => {
    setPermissions(["platform.tenants.manage"]);
    const user = userEvent.setup();
    renderPage();

    await screen.findAllByText("Acme A.Ş.");
    await user.click(screen.getAllByRole("button", { name: trPlatform.edit })[0]);
    await user.clear(await screen.findByLabelText(/^Şirket adı/));
    await user.click(screen.getByRole("button", { name: trCommon.save }));

    expect(await screen.findByRole("alert")).toHaveTextContent(trPlatform.errors.companyRequired);
    expect(updateTenant).not.toHaveBeenCalled();
  });
});
