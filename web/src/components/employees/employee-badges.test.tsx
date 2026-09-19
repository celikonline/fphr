import { beforeAll, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trEmployees from "../../../public/locales/tr/employees.json";
import { AccountStatusBadge, EmployeeStatusBadge } from "./employee-badges";

// senseik-042 (etiketsiz çift rozet): an "Active" employee with an active account renders two badges
// that both read "Aktif" with nothing to tell them apart - the employee card header must label them.

// jsdom has no matchMedia; MantineProvider's color-scheme detection needs one to render.
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
});
function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "employees",
    ns: ["employees"],
    resources: { tr: { employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderBadges(ui: React.ReactElement) {
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>{ui}</MantineProvider>
    </I18nextProvider>,
  );
}

describe("employee status badges (senseik-042)", () => {
  it("both read the same bare text without a label, reproducing the ambiguous pair", () => {
    renderBadges(
      <>
        <EmployeeStatusBadge status="Active" />
        <AccountStatusBadge status="active" />
      </>,
    );

    const badges = screen.getAllByText("Aktif");
    expect(badges).toHaveLength(2);
  });

  it("disambiguates the pair when the employee card header passes a label", () => {
    renderBadges(
      <>
        <EmployeeStatusBadge status="Active" label="Çalışan" />
        <AccountStatusBadge status="active" label="Hesap" />
      </>,
    );

    expect(screen.getByText("Çalışan: Aktif")).toBeInTheDocument();
    expect(screen.getByText("Hesap: Aktif")).toBeInTheDocument();
    expect(screen.queryByText("Aktif")).toBeNull();
  });
});
