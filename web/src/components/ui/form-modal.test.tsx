import { beforeAll, describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
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
});
