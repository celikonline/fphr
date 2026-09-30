import React, { useState } from "react";
import {
  ActivityIndicator,
  Alert,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View
} from "react-native";
import { useNavigation, useRoute, type RouteProp } from "@react-navigation/native";
import { useTranslation } from "react-i18next";
import { palette, radius, text } from "@/theme/theme";
import {
  canAcknowledge,
  canObject,
  payslipService,
  validateObjectionReason
} from "@/services/PayslipService";
import type { AppStackParamList } from "@/types/navigation";
import {
  formatMoney,
  payslipPeriodLabel,
  payslipStatusLabel,
  sharePayslipDocument
} from "@/screens/app/PayrollScreen";

const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 20, xxxl: 32 } as const;

export default function PayslipDetailScreen() {
  const { t } = useTranslation("payroll");
  const navigation = useNavigation();
  const { params } = useRoute<RouteProp<AppStackParamList, "PayslipDetail">>();
  const [payslip, setPayslip] = useState(params.payslip);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [opening, setOpening] = useState(false);

  const openDocument = async () => {
    setOpening(true);
    try {
      await sharePayslipDocument(payslip);
    } finally {
      setOpening(false);
    }
  };

  const acknowledge = async () => {
    setBusy(true);
    try {
      const updated = await payslipService.acknowledge(payslip.payslipId);
      if (updated) setPayslip(updated);
      Alert.alert(t("detail.acknowledgedTitle"), t("detail.acknowledgedMessage"));
    } catch {
      Alert.alert(t("common:alerts.error"), t("detail.acknowledgeError"));
    } finally {
      setBusy(false);
    }
  };

  const submitObjection = async () => {
    const trimmed = validateObjectionReason(reason);
    if (!trimmed) {
      Alert.alert(t("common:alerts.missingInfo"), t("detail.objectionRequired"));
      return;
    }
    setBusy(true);
    try {
      await payslipService.object(payslip.payslipId, trimmed);
      setPayslip({ ...payslip, objectionStatus: "pending", objectionReason: trimmed });
      setReason("");
      Alert.alert(t("common:alerts.sent"), t("detail.objectionSent"));
      navigation.goBack();
    } catch {
      Alert.alert(t("common:alerts.error"), t("detail.objectionError"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>{t("detail.heading", { period: payslipPeriodLabel(payslip) })}</Text>
      <Text style={styles.status}>{payslipStatusLabel(payslip)}</Text>
      <View style={styles.card}>
        <Text style={styles.label}>{t("detail.gross")}</Text>
        <Text style={styles.value}>{formatMoney(payslip.grossPay, payslip.currency)}</Text>
        <Text style={styles.label}>{t("detail.net")}</Text>
        <Text style={styles.value}>{formatMoney(payslip.netPay, payslip.currency)}</Text>
      </View>
      {payslip.objectionReason ? (
        <Text style={styles.status}>{t("detail.objectionReason", { reason: payslip.objectionReason })}</Text>
      ) : null}

      {payslip.objectionStatus === "pending" ? (
        <Text style={styles.status}>{t("detail.objectionPendingNote")}</Text>
      ) : null}
      {payslip.objectionResolutionNote ? (
        <Text style={styles.status}>{t("detail.objectionResult", { note: payslip.objectionResolutionNote })}</Text>
      ) : null}

      <TouchableOpacity
        style={[styles.button, styles.outline, opening && styles.disabled]}
        disabled={opening}
        onPress={openDocument}
        accessibilityRole="button"
      >
        {opening ? (
          <ActivityIndicator color={palette.brand} />
        ) : (
          <Text style={styles.outlineText}>{t("openDocument")}</Text>
        )}
      </TouchableOpacity>

      <TouchableOpacity
        style={[styles.button, (!canAcknowledge(payslip) || busy) && styles.disabled]}
        disabled={!canAcknowledge(payslip) || busy}
        onPress={acknowledge}
        accessibilityRole="button"
      >
        {busy ? <ActivityIndicator color={palette.white} /> : <Text style={styles.buttonText}>{t("detail.acknowledge")}</Text>}
      </TouchableOpacity>

      {canObject(payslip) ? (
        <View style={styles.objection}>
          <Text style={styles.label}>{t("detail.objectionLabel")}</Text>
          <TextInput
            style={styles.input}
            value={reason}
            onChangeText={setReason}
            multiline
            placeholder={t("detail.objectionPlaceholder")}
            placeholderTextColor={palette.textFaint}
          />
          <TouchableOpacity
            style={[styles.button, styles.outline, busy && styles.disabled]}
            disabled={busy}
            onPress={submitObjection}
            accessibilityRole="button"
          >
            <Text style={styles.outlineText}>{t("detail.object")}</Text>
          </TouchableOpacity>
        </View>
      ) : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: palette.night },
  content: { padding: spacing.lg },
  title: { ...text.title, color: palette.white },
  status: { ...text.bodyMuted, color: palette.textMuted, marginVertical: spacing.sm },
  card: {
    backgroundColor: palette.glass,
    borderRadius: radius.button,
    padding: spacing.md,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    marginVertical: spacing.md
  },
  label: { ...text.bodyMuted, color: palette.textMuted, marginTop: spacing.sm },
  value: { ...text.heading, color: palette.white },
  button: {
    backgroundColor: palette.brand,
    borderRadius: radius.button,
    padding: spacing.md,
    alignItems: "center",
    marginTop: spacing.md
  },
  buttonText: { ...text.body, color: palette.white, fontWeight: "600" },
  disabled: { opacity: 0.5 },
  objection: { marginTop: spacing.lg },
  input: {
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
    padding: spacing.md,
    minHeight: 80,
    textAlignVertical: "top",
    marginTop: spacing.xs,
    color: palette.white
  },
  outline: { backgroundColor: palette.glass, borderWidth: 1, borderColor: palette.brand },
  outlineText: { ...text.body, color: palette.brand, fontWeight: "600" }
});
