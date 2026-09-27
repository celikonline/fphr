import React, { useEffect, useState } from "react";
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from "react-native";
import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ErrorBanner, IconCircleButton } from "@/components/ui";
import { LeaveRequestForm } from "@/components/leave/LeaveRequestForm";
import OvertimeRequestSection from "@/components/requests/OvertimeRequestSection";
import ExpenseClaimSection from "@/components/requests/ExpenseClaimSection";
import AdvanceRequestSection from "@/components/requests/AdvanceRequestSection";
import { leaveService, type RequestableLeaveType } from "@/services/LeaveService";
import { palette, radius, text } from "@/theme/theme";
import type { AppStackParamList, RequestKind } from "@/types/navigation";

type Props = NativeStackScreenProps<AppStackParamList, "NewRequest">;

export const NEW_REQUEST_TITLES: Record<RequestKind, string> = {
  leave: "İzin talebi",
  overtime: "Fazla mesai talebi",
  expense: "Masraf talebi",
  advance: "Avans talebi",
};

/**
 * Full-screen request form opened from the tab bar's "Yeni talep" sheet: a brand-blue title band
 * (with a close button) over a dark rounded body that hosts the existing form for `kind`. The forms
 * themselves are restyled in Task 7; here they are only placed into the new shell.
 */
export default function NewRequestScreen({ navigation, route }: Props) {
  const { kind } = route.params;
  const insets = useSafeAreaInsets();
  const close = () => navigation.goBack();

  return (
    <View style={styles.root}>
      <View style={[styles.header, { paddingTop: insets.top + 12 }]}>
        <Text style={styles.title} numberOfLines={1}>
          {NEW_REQUEST_TITLES[kind]}
        </Text>
        <IconCircleButton icon="close" onPress={close} accessibilityLabel="Kapat" />
      </View>
      <View style={styles.body}>
        <ScrollView
          contentContainerStyle={[styles.content, { paddingBottom: insets.bottom + 24 }]}
          keyboardShouldPersistTaps="handled"
        >
          <RequestForm kind={kind} onSubmitted={close} />
        </ScrollView>
      </View>
    </View>
  );
}

/**
 * Mounts the existing form for `kind` with the same props its current host passes (`LeaveScreen`
 * for leave; `RequestsScreen` renders the other three prop-less). Only `LeaveRequestForm` exposes a
 * success callback today, so only a leave submit closes the screen automatically.
 */
function RequestForm({ kind, onSubmitted }: { kind: RequestKind; onSubmitted: () => void }) {
  switch (kind) {
    case "leave":
      return <LeaveRequestBody onSubmitted={onSubmitted} />;
    case "overtime":
      return <OvertimeRequestSection />;
    case "expense":
      return <ExpenseClaimSection />;
    case "advance":
      return <AdvanceRequestSection />;
  }
}

/** `LeaveRequestForm` needs the requestable leave types up front (`LeaveScreen` loads them the same
 * way); render it only once they have arrived so its initial type selection sees the full list. */
function LeaveRequestBody({ onSubmitted }: { onSubmitted: () => void }) {
  const [types, setTypes] = useState<RequestableLeaveType[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    leaveService
      .getMyRequestableLeaveTypes()
      .then((result) => {
        if (active) setTypes(result);
      })
      .catch(() => {
        if (active) setError("İzin türleri yüklenemedi. Lütfen tekrar deneyin.");
      });
    return () => {
      active = false;
    };
  }, []);

  if (error) return <ErrorBanner message={error} />;
  if (!types) return <ActivityIndicator size="large" color={palette.white} />;
  return <LeaveRequestForm requestableTypes={types} onSubmitted={onSubmitted} />;
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: palette.brand,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    paddingHorizontal: 20,
    paddingBottom: 18,
  },
  title: {
    ...text.title,
    flex: 1,
  },
  body: {
    flex: 1,
    overflow: "hidden",
    borderTopLeftRadius: radius.sheet,
    borderTopRightRadius: radius.sheet,
    backgroundColor: palette.sheetBottom,
    // New-Architecture-only style prop (RN 0.86); falls back to the solid `backgroundColor` above.
    experimental_backgroundImage: `linear-gradient(180deg, ${palette.sheetTop}, ${palette.sheetBottom})`,
  },
  content: {
    padding: 20,
  },
});
