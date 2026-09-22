import { beforeAll, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trAssets from "../../../public/locales/tr/assets.json";
import ReplacementDueBadge from "./replacement-due-badge";
import { isReplacementOverdue, type Asset } from "@/services/assets.service";

const helmet: Asset = {
  id: "a1",
  categoryId: "ppe",
  name: "Baret",
  serialNumber: null,
  description: null,
  status: "assigned",
  assignedEmployeeId: "e1",
  issuedAt: "2025-01-01",
  returnedAt: null,
  replacementDueOn: "2026-01-01",
};

// jsdom has no matchMedia; MantineProvider reads it for the color scheme.
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

function renderBadge(asset: Asset) {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    ns: ["assets"],
    resources: { tr: { assets: trAssets } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return render(
    <I18nextProvider i18n={instance}>
      <MantineProvider>
        <ReplacementDueBadge asset={asset} />
      </MantineProvider>
    </I18nextProvider>
  );
}

describe("ReplacementDueBadge", () => {
  it("flags a KKD item whose replacement date has passed", () => {
    renderBadge(helmet);

    expect(screen.getByText(/Yenileme gecikti/)).toBeInTheDocument();
  });

  it("renders nothing for equipment without periodic replacement", () => {
    renderBadge({ ...helmet, replacementDueOn: undefined });

    expect(screen.queryByText(/Yenileme/)).not.toBeInTheDocument();
  });

  it("compares yyyy-MM-dd dates against today", () => {
    expect(isReplacementOverdue(helmet, "2026-01-01")).toBe(false);
    expect(isReplacementOverdue(helmet, "2026-01-02")).toBe(true);
  });
});
