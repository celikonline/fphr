import React from "react";
import { act, create } from "react-test-renderer";
import { AccessibilityInfo, Platform } from "react-native";
import { AuthFormError } from "@/components/auth/AuthForm";

const announceSpy = AccessibilityInfo.announceForAccessibility as jest.Mock;

describe("AuthFormError", () => {
  beforeEach(() => {
    Platform.OS = "ios";
    announceSpy.mockClear();
  });

  afterEach(() => {
    Platform.OS = "ios";
  });

  it("announces the message to VoiceOver on iOS", () => {
    act(() => {
      create(<AuthFormError message="E-posta veya şifre hatalı. Lütfen tekrar deneyin." />);
    });

    expect(announceSpy).toHaveBeenCalledWith("E-posta veya şifre hatalı. Lütfen tekrar deneyin.");
  });

  it("re-announces when the message text changes", () => {
    let renderer: ReturnType<typeof create>;

    act(() => {
      renderer = create(<AuthFormError message="İlk hata" />);
    });
    act(() => {
      renderer.update(<AuthFormError message="İkinci hata" />);
    });

    expect(announceSpy).toHaveBeenNthCalledWith(1, "İlk hata");
    expect(announceSpy).toHaveBeenNthCalledWith(2, "İkinci hata");
  });

  it("does not call announceForAccessibility on Android (accessibilityLiveRegion covers it there)", () => {
    Platform.OS = "android";

    act(() => {
      create(<AuthFormError message="Bir hata oluştu." />);
    });

    expect(announceSpy).not.toHaveBeenCalled();
  });
});
