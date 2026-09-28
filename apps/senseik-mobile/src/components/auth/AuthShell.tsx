import React from "react";
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { AuthBackdrop } from "./AuthBackdrop";
import { BrandMark } from "./BrandMark";
import { Wordmark } from "./Wordmark";

/** Compact brand hero (mark + wordmark) above the auth panels other than the login form. */
export function AuthHero() {
  return (
    <View style={styles.hero}>
      <BrandMark size={56} />
      <Wordmark />
    </View>
  );
}

/**
 * Layout of the secondary auth screens (forgot password, biometric prompt): the login screen's
 * backdrop and compact brand hero, with the screen's glass `AuthPanel` rendered below it.
 */
export function AuthShell({ children }: { children: React.ReactNode }) {
  return (
    <AuthBackdrop>
      <SafeAreaView style={styles.flexOne} edges={["top", "bottom"]}>
        <KeyboardAvoidingView
          style={styles.flexOne}
          behavior={Platform.OS === "ios" ? "padding" : undefined}
        >
          <ScrollView
            contentContainerStyle={styles.scrollContent}
            keyboardShouldPersistTaps="handled"
          >
            <AuthHero />
            {children}
          </ScrollView>
        </KeyboardAvoidingView>
      </SafeAreaView>
    </AuthBackdrop>
  );
}

const styles = StyleSheet.create({
  flexOne: { flex: 1 },
  scrollContent: {
    flexGrow: 1,
    justifyContent: "center",
    paddingHorizontal: 16,
    paddingVertical: 24,
  },
  hero: { paddingHorizontal: 12, alignItems: "flex-start", gap: 12 },
});
