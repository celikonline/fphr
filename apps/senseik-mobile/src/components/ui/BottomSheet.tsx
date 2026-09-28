import React, { type ReactNode } from "react";
import {
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { useReducedMotion } from "react-native-reanimated";
import { useTranslation } from "react-i18next";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  visible: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
};

/** Modal-based bottom sheet (new request picker, date picker, reject-reason forms, ...). The
 * scrim closes on tap; the Android hardware back button closes it via `onRequestClose`. On iOS the
 * sheet is padded up by the keyboard's height so a focused input and its buttons stay visible. */
export function BottomSheet({ visible, onClose, title, children }: Props) {
  const { t } = useTranslation();
  const reduceMotion = useReducedMotion();

  return (
    <Modal
      visible={visible}
      transparent
      animationType={reduceMotion ? "none" : "slide"}
      onRequestClose={onClose}
    >
      <KeyboardAvoidingView
        style={styles.scrim}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
      >
        <Pressable
          style={StyleSheet.absoluteFill}
          onPress={onClose}
          accessibilityRole="button"
          accessibilityLabel={t("actions.close")}
        />
        <View style={styles.sheet}>
          <View style={styles.grab} />
          {title ? <Text style={styles.title}>{title}</Text> : null}
          {children}
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  scrim: {
    flex: 1,
    justifyContent: "flex-end",
    backgroundColor: palette.scrim,
  },
  sheet: {
    borderTopLeftRadius: radius.sheet,
    borderTopRightRadius: radius.sheet,
    borderWidth: 1,
    borderBottomWidth: 0,
    borderColor: palette.glassBorder,
    backgroundColor: palette.sheetBottom,
    // New-Architecture-only style prop (RN 0.86); falls back to the solid `backgroundColor` above.
    experimental_backgroundImage: `linear-gradient(180deg, ${palette.sheetTop}, ${palette.sheetBottom})`,
    padding: 20,
    paddingBottom: 32,
  },
  grab: {
    width: 40,
    height: 5,
    borderRadius: 3,
    backgroundColor: palette.grabber,
    alignSelf: "center",
    marginBottom: 14,
  },
  title: {
    ...text.heading,
    marginBottom: 6,
  },
});
