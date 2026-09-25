import { beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trShifts from "../../../public/locales/tr/shifts.json";
import ShiftDefinitionForm, { emptyShiftDefinition } from "./shift-definition-form";

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (q: string) => ({
      matches: false,
      media: q,
      addEventListener() {},
      removeEventListener() {},
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

async function renderForm(onSubmit = vi.fn(), initial = emptyShiftDefinition()) {
  const i18n = i18next.createInstance();
  await i18n.init({ lng: "tr", resources: { tr: { shifts: trShifts } }, defaultNS: "shifts" });
  render(
    <I18nextProvider i18n={i18n}>
      <MantineProvider>
        <ShiftDefinitionForm
          initial={initial}
          saving={false}
          onSubmit={onSubmit}
          onCancel={() => {}}
        />
      </MantineProvider>
    </I18nextProvider>
  );
  return onSubmit;
}

describe("ShiftDefinitionForm", () => {
  it("multiple_breaks_editable: add, edit and remove breaks", async () => {
    const user = userEvent.setup();
    const onSubmit = await renderForm();
    await user.click(screen.getByRole("button", { name: "Mola ekle" }));
    await user.click(screen.getByRole("button", { name: "Mola ekle" }));
    expect(screen.getAllByTestId("break-row")).toHaveLength(2);
    const first = screen.getAllByTestId("break-row")[0];
    const dur = within(first).getByLabelText("Süre (dk)");
    await user.clear(dur);
    await user.type(dur, "45");
    await user.click(
      within(screen.getAllByTestId("break-row")[1]).getByRole("button", { name: "Molayı sil" })
    );
    expect(screen.getAllByTestId("break-row")).toHaveLength(1);
    await user.type(screen.getByLabelText("Kod"), "GUN");
    await user.type(screen.getByLabelText("Ad"), "Gündüz");
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
    const [dto, applyToPast] = onSubmit.mock.calls[0];
    expect(applyToPast).toBe(false);
    expect(dto.breaks).toEqual([expect.objectContaining({ durationMinutes: 45 })]);
  });

  it("save_and_apply_past: submits with applyToPast true", async () => {
    const user = userEvent.setup();
    const onSubmit = await renderForm();
    await user.type(screen.getByLabelText("Kod"), "GUN");
    await user.type(screen.getByLabelText("Ad"), "Gündüz");
    await user.click(screen.getByRole("button", { name: "Kaydet ve geçmişe uygula" }));
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ code: "GUN", name: "Gündüz" }),
      true
    );
  });

  it("does not submit without code and name", async () => {
    const user = userEvent.setup();
    const onSubmit = await renderForm();
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("flexible_hides_fixed_end: flexible shows duration and window, hides end", async () => {
    await renderForm(vi.fn(), { ...emptyShiftDefinition(), type: "flexible" });
    expect(screen.queryByRole("slider", { name: "Bitiş saati" })).toBeNull();
    expect(screen.getByLabelText("Vardiya süresi (dk)")).toBeInTheDocument();
    expect(screen.getByLabelText("En erken giriş")).toBeInTheDocument();
  });

  it("fixed shows end slider and no flexible fields", async () => {
    await renderForm();
    expect(screen.getByRole("slider", { name: "Bitiş saati" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Vardiya süresi (dk)")).toBeNull();
  });
});
