import { Linking } from "react-native";
import { openPersonEmail, openPersonPhone } from "@/screens/app/PeopleScreen";

/**
 * `apps/senseik-mobile` has no React Native component-render harness (no
 * `@testing-library/react-native` dependency yet) - adding one is a new dependency and out of
 * scope for TK-024 (surgical change rule). These tests instead cover PeopleScreen's tap-to-contact
 * behavior directly: the exported `openPersonPhone`/`openPersonEmail` helpers are exactly what each
 * row's `onPress` calls, so this verifies the acceptance criterion (tel:/mailto: on tap) without a
 * full render.
 */
describe("PeopleScreen contact helpers", () => {
  beforeEach(() => {
    jest.spyOn(Linking, "openURL").mockClear().mockResolvedValue(undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  describe("openPersonPhone", () => {
    it("opens a tel: link when a work phone is present", () => {
      openPersonPhone("+90 555 000 00 00");
      expect(Linking.openURL).toHaveBeenCalledWith("tel:+90 555 000 00 00");
    });

    it("does nothing when there is no work phone", () => {
      openPersonPhone(null);
      expect(Linking.openURL).not.toHaveBeenCalled();
    });
  });

  describe("openPersonEmail", () => {
    it("opens a mailto: link when a work e-mail is present", () => {
      openPersonEmail("ayse.yilmaz@ornek.com");
      expect(Linking.openURL).toHaveBeenCalledWith("mailto:ayse.yilmaz@ornek.com");
    });

    it("does nothing when there is no work e-mail", () => {
      openPersonEmail(undefined);
      expect(Linking.openURL).not.toHaveBeenCalled();
    });
  });
});
