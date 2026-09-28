import React, { useEffect, useState } from "react";
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from "react-native";
import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { ErrorBanner, IconCircleButton } from "@/components/ui";
import { LeaveRequestForm } from "@/components/leave/LeaveRequestForm";
import OvertimeRequestSection from "@/components/requests/OvertimeRequestSection";
import ExpenseClaimSection from "@/components/requests/ExpenseClaimSection";
import AdvanceRequestSection from "@/components/requests/AdvanceRequestSection";
import { leaveService, type RequestableLeaveType } from "@/services/LeaveService";
import { palette, radius, text } from "@/theme/theme";
import type { AppStackParamList, RequestKind } from "@/types/navigation";

type Props = NativeStackScreenProps<AppStackParamList, "NewRequest">;

/** Header title for a request kind ("İzin talebi"), in the active language. */
export function newRequestTitle(kind: RequestKind): string {
  return i18n.t(`requests:titles.${kind}`);
}

/**
 * Full-screen request form opened from the tab bar's "Yeni talep" sheet: a brand-blue title band
 * (with a close button) over a dark rounded body that hosts the existing form for `kind`. The forms
 * themselves use the glass fields, the JS "Tarih seç" sheet and a full-width "Talebi gönder" button.
 */
export default function NewRequestScreen({ navigation, route }: Props) {
  const { t } = useTranslation();
  const { kind } = route.params;
  const insets = useSafeAreaInsets();
  const close = () => navigation.goBack();

  return (
    <View style={styles.root}>
      <View style={[styles.header, { paddingTop: insets.top + 12 }]}>
        <Text style={styles.title} numberOfLines={1}>
          {newRequestTitle(kind)}
        </Text>
        <IconCircleButton icon="close" onPress={close} accessibilityLabel={t("actions.close")} />
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

/** Mounts the form for `kind`; every form calls `onSubmitted` after a successful submit, which
 * closes the screen (Taleplerim/İzinlerim reload on focus). */
function RequestForm({ kind, onSubmitted }: { kind: RequestKind; onSubmitted: () => void }) {
  switch (kind) {
    case "leave":
      return <LeaveRequestBody onSubmitted={onSubmitted} />;
    case "overtime":
      return <OvertimeRequestSection onSubmitted={onSubmitted} />;
    case "expense":
      return <ExpenseClaimSection onSubmitted={onSubmitted} />;
    case "advance":
      return <AdvanceRequestSection onSubmitted={onSubmitted} />;
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
        if (active) setError(i18n.t("requests:leaveTypesLoadError"));
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
