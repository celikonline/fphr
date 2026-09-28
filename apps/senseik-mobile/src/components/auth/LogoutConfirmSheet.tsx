import React, { useEffect, useRef, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { useAuthStore } from "@/store/authStore";
import { BottomSheet } from "@/components/ui/BottomSheet";
import { PrimaryButton } from "@/components/ui/PrimaryButton";
import { text } from "@/theme/theme";
import { AuthFormError } from "./AuthForm";

const LOGOUT_FAILED_MESSAGE = "Çıkış yapılamadı. Lütfen tekrar deneyin.";

type Props = {
  visible: boolean;
  onClose: () => void;
};

/**
 * "Çıkış yapılsın mı?" confirmation (Profile's button, Home's ⋮ menu). While `logout()` runs the
 * danger button shows a spinner, repeated taps are ignored and the sheet cannot be dismissed. On
 * success the store drops the session and the navigator swaps to the auth stack (unmounting this);
 * on failure the user stays signed in and sees an inline error.
 */
export function LogoutConfirmSheet({ visible, onClose }: Props) {
  const { logout } = useAuthStore();
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Ref, not state: two taps in the same frame both see the stale `isLoggingOut === false`.
  const inFlightRef = useRef(false);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const handleConfirm = async () => {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setIsLoggingOut(true);
    setError(null);
    try {
      await logout();
    } catch {
      if (mountedRef.current) setError(LOGOUT_FAILED_MESSAGE);
    } finally {
      inFlightRef.current = false;
      if (mountedRef.current) setIsLoggingOut(false);
    }
  };

  const handleClose = () => {
    if (inFlightRef.current) return;
    setError(null);
    onClose();
  };

  return (
    <BottomSheet visible={visible} onClose={handleClose} title="Çıkış yapılsın mı?">
      <Text style={styles.body}>Tekrar giriş yapmanız gerekecek.</Text>
      {error ? <AuthFormError message={error} /> : null}
      <View style={styles.actions}>
        <PrimaryButton
          label="Çıkış yap"
          variant="danger"
          loading={isLoggingOut}
          onPress={() => void handleConfirm()}
        />
        <PrimaryButton
          label="Vazgeç"
          variant="secondary"
          disabled={isLoggingOut}
          onPress={handleClose}
        />
      </View>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  body: { ...text.bodyMuted },
  actions: { marginTop: 20, gap: 12 },
});
