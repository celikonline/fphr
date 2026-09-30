import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { FormModal } from "./form-modal";

// jsdom has no matchMedia; MantineProvider's color-scheme detection needs it mounted to render.
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

// senseik-021: form/edit modals should open left-aligned, not horizontally centered (Mantine's
// `Modal` default). `FormModal` is the shared wrapper every form dialog should use instead of the
// bare Mantine `Modal` so the positioning fix lives in one place.
describe("FormModal", () => {
  it("left-aligns the modal via the inner wrapper's flex alignment", () => {
    const { getByRole } = render(
      <MantineProvider>
        <FormModal opened onClose={() => {}} title="Test form">
          <div>content</div>
        </FormModal>
      </MantineProvider>,
    );

    // Mantine's ModalBaseContent renders the "inner" flex wrapper as the dialog's direct parent
    // (FocusTrap clones its child rather than adding a DOM node of its own).
    const inner = getByRole("dialog").parentElement;
    expect(inner).not.toBeNull();
    expect(inner?.style.justifyContent).toBe("flex-start");
  });

  it("still renders the title and content like a plain Modal", () => {
    const { getByText, getByRole } = render(
      <MantineProvider>
        <FormModal opened onClose={() => {}} title="Test form">
          <div>form body</div>
        </FormModal>
      </MantineProvider>,
    );

    expect(getByRole("dialog")).not.toBeNull();
    expect(getByText("Test form")).not.toBeNull();
    expect(getByText("form body")).not.toBeNull();
  });

  it("omitting_prop_renders_as_before: no extra button without onSaveAndNew", () => {
    render(
      <MantineProvider>
        <FormModal opened onClose={() => {}} title="Test form">
          <div>content</div>
        </FormModal>
      </MantineProvider>,
    );

    expect(screen.queryByTestId("form-modal-save-and-new")).toBeNull();
    expect(screen.getAllByRole("button")).toHaveLength(1); // only the close button
  });

  it("save_and_new_resets_form_keeps_modal: click calls handler, modal stays open", async () => {
    const onSaveAndNew = vi.fn();
    const onClose = vi.fn();
    const i18n = i18next.createInstance();
    void i18n.init({
      lng: "tr",
      resources: { tr: { common: { saveAndNew: "Oluştur ve Yeni", close: "Kapat" } } },
      defaultNS: "common",
      react: { useSuspense: false },
    });
    render(
      <I18nextProvider i18n={i18n}>
        <MantineProvider>
          <FormModal opened onClose={onClose} title="Test form" onSaveAndNew={onSaveAndNew}>
            <div>content</div>
          </FormModal>
        </MantineProvider>
      </I18nextProvider>,
    );

    await userEvent.setup().click(screen.getByRole("button", { name: "Oluştur ve Yeni" }));
    expect(onSaveAndNew).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).not.toBeNull();
  });
});
