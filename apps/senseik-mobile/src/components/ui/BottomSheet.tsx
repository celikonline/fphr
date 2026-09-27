import React, { type ReactNode } from "react";
import { Modal, Pressable, StyleSheet, Text, View } from "react-native";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  visible: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
};

/** Modal-based bottom sheet (new request picker, date picker, reject-reason forms, ...). The
 * scrim closes on tap; the Android hardware back button closes it via `onRequestClose`. */
export function BottomSheet({ visible, onClose, title, children }: Props) {
  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View style={styles.scrim}>
        <Pressable
          style={StyleSheet.absoluteFill}
          onPress={onClose}
          accessibilityRole="button"
          accessibilityLabel="Kapat"
        />
        <View style={styles.sheet}>
          <View style={styles.grab} />
          {title ? <Text style={styles.title}>{title}</Text> : null}
          {children}
        </View>
      </View>
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
