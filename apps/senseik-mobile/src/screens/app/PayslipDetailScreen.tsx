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
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  canAcknowledge,
  canObject,
  payslipService,
  validateObjectionReason
} from "@/services/PayslipService";
import type { AppStackParamList } from "@/types/navigation";
import { formatTry, payslipPeriodLabel, payslipStatusLabel } from "@/screens/app/PayrollScreen";

export default function PayslipDetailScreen() {
  const navigation = useNavigation();
  const { params } = useRoute<RouteProp<AppStackParamList, "PayslipDetail">>();
  const [payslip, setPayslip] = useState(params.payslip);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);

  const acknowledge = async () => {
    setBusy(true);
    try {
      await payslipService.acknowledge(payslip.payslipId);
      setPayslip({ ...payslip, acknowledgedAt: new Date().toISOString() });
      Alert.alert("Onaylandı", "Bordronuzu onayladınız.");
    } catch {
      Alert.alert("Hata", "Bordro onaylanamadı. Lütfen tekrar deneyin.");
    } finally {
      setBusy(false);
    }
  };

  const submitObjection = async () => {
    const trimmed = validateObjectionReason(reason);
    if (!trimmed) {
      Alert.alert("Eksik bilgi", "İtiraz için gerekçe yazmanız zorunludur.");
      return;
    }
    setBusy(true);
    try {
      await payslipService.object(payslip.payslipId, trimmed);
      setPayslip({ ...payslip, objectionStatus: "pending", objectionReason: trimmed });
      setReason("");
      Alert.alert("Gönderildi", "İtirazınız iletildi.");
      navigation.goBack();
    } catch {
      Alert.alert("Hata", "İtiraz gönderilemedi. Lütfen tekrar deneyin.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>{payslipPeriodLabel(payslip)} bordrosu</Text>
      <Text style={styles.status}>{payslipStatusLabel(payslip)}</Text>
      <View style={styles.card}>
        <Text style={styles.label}>Brüt ücret</Text>
        <Text style={styles.value}>{formatTry(payslip.grossPay)}</Text>
        <Text style={styles.label}>Net ücret</Text>
        <Text style={styles.value}>{formatTry(payslip.netPay)}</Text>
      </View>
      {payslip.objectionReason ? (
        <Text style={styles.status}>İtiraz gerekçeniz: {payslip.objectionReason}</Text>
      ) : null}

      <TouchableOpacity
        style={[styles.button, (!canAcknowledge(payslip) || busy) && styles.disabled]}
        disabled={!canAcknowledge(payslip) || busy}
        onPress={acknowledge}
        accessibilityRole="button"
      >
        {busy ? <ActivityIndicator color={colors.white} /> : <Text style={styles.buttonText}>Onayla</Text>}
      </TouchableOpacity>

      {canObject(payslip) ? (
        <View style={styles.objection}>
          <Text style={styles.label}>İtiraz gerekçesi</Text>
          <TextInput
            style={styles.input}
            value={reason}
            onChangeText={setReason}
            multiline
            placeholder="İtiraz nedeninizi yazın"
          />
          <TouchableOpacity
            style={[styles.button, styles.outline, busy && styles.disabled]}
            disabled={busy}
            onPress={submitObjection}
            accessibilityRole="button"
          >
            <Text style={styles.outlineText}>İtiraz et</Text>
          </TouchableOpacity>
        </View>
      ) : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg },
  title: { ...typography.h3, color: colors.text },
  status: { ...typography.bodySm, color: colors.textSecondary, marginVertical: spacing.sm },
  card: {
    backgroundColor: colors.white,
    borderRadius: 8,
    padding: spacing.md,
    borderWidth: 1,
    borderColor: colors.gray200,
    marginVertical: spacing.md
  },
  label: { ...typography.bodySm, color: colors.textSecondary, marginTop: spacing.sm },
  value: { ...typography.h4, color: colors.text },
  button: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    padding: spacing.md,
    alignItems: "center",
    marginTop: spacing.md
  },
  buttonText: { ...typography.body, color: colors.white, fontWeight: "600" },
  disabled: { opacity: 0.5 },
  objection: { marginTop: spacing.lg },
  input: {
    borderWidth: 1,
    borderColor: colors.gray300,
    borderRadius: 8,
    padding: spacing.md,
    minHeight: 80,
    textAlignVertical: "top",
    marginTop: spacing.xs,
    color: colors.text
  },
  outline: { backgroundColor: colors.white, borderWidth: 1, borderColor: colors.primary },
  outlineText: { ...typography.body, color: colors.primary, fontWeight: "600" }
});
