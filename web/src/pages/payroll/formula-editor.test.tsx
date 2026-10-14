import { useState } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { FormulaEditor } from "./formula-editor";
import { getFormulaCatalog, validatePayItemFormula } from "@/services/payroll-pay-items.service";

vi.mock("@/services/payroll-pay-items.service", () => ({
  getFormulaCatalog: vi.fn(),
  validatePayItemFormula: vi.fn(),
}));

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

beforeEach(() => {
  vi.mocked(getFormulaCatalog).mockReset().mockResolvedValue({
    variables: [
      { name: "brut", sampleValue: 20000 },
      { name: "deger", sampleValue: 100 },
    ],
    functions: [{ name: "min", aliases: ["en_kucuk"], minArgs: 2 }, { name: "yuvarla", aliases: ["round"], minArgs: 1, maxArgs: 2 }],
  });
  vi.mocked(validatePayItemFormula).mockReset();
});

function Harness({ initial = "" }: { initial?: string }) {
  const [value, setValue] = useState(initial);
  return <FormulaEditor value={value} onChange={setValue} amountBasis="gross" />;
}

function renderEditor(initial = "") {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "payroll",
    ns: ["payroll", "common"],
    resources: { tr: { payroll: trPayroll, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <Harness initial={initial} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("FormulaEditor", () => {
  it("lists catalog variables and functions and inserts them on click", async () => {
    renderEditor("1 + ");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "brut değişkenini ekle" }));
    expect(screen.getByRole("textbox", { name: /^Formül/ })).toHaveValue("1 + brut");

    await user.click(screen.getByRole("button", { name: "min fonksiyonunu ekle" }));
    expect(screen.getByRole("textbox", { name: /^Formül/ })).toHaveValue("1 + brutmin()");
    expect(screen.getByText("Esas brüt ücret (kıst/oranlama uygulanmış; fazla mesai ve diğer kalemler hariç)", { exact: false })).toBeInTheDocument();
  });

  it("sends the formula with the catalog sample values and shows used variables and sample result", async () => {
    vi.mocked(validatePayItemFormula).mockResolvedValue({ isValid: true, usedVariables: ["brut"], sampleResult: 2000 });
    renderEditor("brut * 0.1");
    const user = userEvent.setup();

    await screen.findByRole("button", { name: "brut değişkenini ekle" });
    await user.click(screen.getByRole("button", { name: "Doğrula" }));

    expect(await screen.findByText("Formül geçerli")).toBeInTheDocument();
    expect(screen.getByText(/Kullanılan değişkenler: brut/)).toBeInTheDocument();
    expect(screen.getByText(/^2\.?000$/)).toBeInTheDocument();
    expect(validatePayItemFormula).toHaveBeenCalledWith({ formula: "brut * 0.1", amountBasis: "gross", sampleValues: { brut: 20000, deger: 100 } });
  });

  it("shows the server error message and highlights the 1-based position", async () => {
    vi.mocked(validatePayItemFormula).mockResolvedValue({
      isValid: false,
      errorCode: "payroll.formula.unknown_variable",
      errorMessage: "Bilinmeyen değişken: xyz",
      position: 8,
    });
    renderEditor("brut + xyz");
    const user = userEvent.setup();

    await screen.findByRole("button", { name: "brut değişkenini ekle" });
    await user.click(screen.getByRole("button", { name: "Doğrula" }));

    expect(await screen.findByText("Formül geçersiz")).toBeInTheDocument();
    expect(screen.getByText("Bilinmeyen değişken: xyz")).toBeInTheDocument();
    expect(screen.getByText("Hata konumu: 8. karakter")).toBeInTheDocument();
    expect(screen.getByTestId("formula-error-mark")).toHaveTextContent("x");
    expect(screen.getByTestId("formula-error-context")).toHaveTextContent("brut + xyz");
  });

  it("tolerates a result without optional fields", async () => {
    vi.mocked(validatePayItemFormula).mockResolvedValue({ isValid: true });
    renderEditor("1");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Doğrula" }));
    expect(await screen.findByText("Formül geçerli")).toBeInTheDocument();
    expect(screen.getByText(/Kullanılan değişkenler: yok/)).toBeInTheDocument();
  });

  it("disables validation for an empty formula and clears the result on edit", async () => {
    vi.mocked(validatePayItemFormula).mockResolvedValue({ isValid: true, usedVariables: [], sampleResult: 1 });
    renderEditor("");
    const user = userEvent.setup();

    expect(screen.getByRole("button", { name: "Doğrula" })).toBeDisabled();
    await user.type(screen.getByRole("textbox", { name: /^Formül/ }), "1");
    await user.click(screen.getByRole("button", { name: "Doğrula" }));
    await screen.findByText("Formül geçerli");
    await user.type(screen.getByRole("textbox", { name: /^Formül/ }), "2");
    await waitFor(() => expect(screen.queryByText("Formül geçerli")).not.toBeInTheDocument());
  });

  it("shows a short error when the catalog cannot be loaded", async () => {
    vi.mocked(getFormulaCatalog).mockReset().mockRejectedValue(new Error("boom"));
    renderEditor("1");

    expect(await screen.findByTestId("formula-catalog-error")).toHaveTextContent("Formül kataloğu yüklenemedi");
  });

  it("does not show a late validation response after the formula changes", async () => {
    let finish!: (value: { isValid: boolean; sampleResult: number }) => void;
    vi.mocked(validatePayItemFormula).mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    renderEditor("brut * 0.1");
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Doğrula" }));
    await user.type(screen.getByRole("textbox", { name: /^Formül/ }), " + 500");
    finish({ isValid: true, sampleResult: 2000 });

    await waitFor(() => expect(screen.getByRole("button", { name: "Doğrula" })).toBeEnabled());
    expect(screen.queryByText("Formül geçerli")).not.toBeInTheDocument();
    expect(screen.queryByText(/^2\.?000$/)).not.toBeInTheDocument();
  });
});
