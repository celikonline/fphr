import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  PUNCH_STATUS_LABEL,
  canCancelPunchRequest,
  isFutureTime,
  parseLocalDateTime,
  punchRequestService,
  type PunchRequest,
  type PunchRequestDirection,
  type PunchRequestStatus,
} from "@/services/PunchRequestService";

const STATUS_COLOR: Record<PunchRequestStatus, string> = {
  pending: colors.warning,
  approved: colors.success,
  rejected: colors.error,
  cancelled: colors.textTertiary,
};

function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function formatOccurred(value: string): string {
  return new Date(value).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" });
}

/** Eksik kayıt talebi (Z2.1): form + kendi talepleri; yalnız bekleyen talep iptal edilir. */
export default function PunchRequestScreen() {
  const [requests, setRequests] = useState<PunchRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [date, setDate] = useState(todayDateOnly());
  const [time, setTime] = useState("");
  const [direction, setDirection] = useState<PunchRequestDirection>("in");
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      setRequests(await punchRequestService.listMine());
    } catch {
      setError("Talepler yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    void load().finally(() => setLoading(false));
  }, [load]);

  async function submit() {
    const occurredAt = parseLocalDateTime(date, time);
    if (!occurredAt) {
      Alert.alert("Eksik bilgi", "Lütfen geçerli bir tarih (yyyy-aa-gg) ve saat (ss:dd) girin.");
      return;
    }
    if (isFutureTime(occurredAt)) {
      Alert.alert("Geçersiz zaman", "Gelecek bir zaman için talep oluşturulamaz.");
      return;
    }
    if (reason.trim().length === 0) {
      Alert.alert("Eksik bilgi", "Gerekçe zorunludur.");
      return;
    }
    setSubmitting(true);
    try {
      await punchRequestService.create({
        occurredAtUtc: occurredAt.toISOString(),
        direction,
        reason: reason.trim(),
      });
      setReason("");
      setTime("");
      await load();
      Alert.alert("Gönderildi", "Talebiniz onaya gönderildi.");
    } catch {
      Alert.alert("Hata", "Talep gönderilemedi. Lütfen tekrar deneyin.");
    } finally {
      setSubmitting(false);
    }
  }

  async function cancel(request: PunchRequest) {
    if (!canCancelPunchRequest(request.status)) return;
    try {
      await punchRequestService.cancel(request.id);
      await load();
    } catch {
      Alert.alert("Hata", "Talep iptal edilemedi. Lütfen tekrar deneyin.");
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>Kayıt talebi</Text>
      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <View style={styles.card}>
        <Text style={styles.fieldLabel}>Tarih (yyyy-aa-gg)</Text>
        <TextInput
          testID="punch-date"
          style={styles.input}
          value={date}
          onChangeText={setDate}
          autoCapitalize="none"
          placeholder="2026-09-20"
        />
        <Text style={styles.fieldLabel}>Saat (ss:dd)</Text>
        <TextInput
          testID="punch-time"
          style={styles.input}
          value={time}
          onChangeText={setTime}
          autoCapitalize="none"
          placeholder="08:30"
        />
        <Text style={styles.fieldLabel}>Yön</Text>
        <View style={styles.row}>
          {(["in", "out"] as const).map((value) => (
            <TouchableOpacity
              key={value}
              testID={`punch-direction-${value}`}
              style={[styles.chip, direction === value && styles.chipSelected]}
              onPress={() => setDirection(value)}
            >
              <Text style={[styles.chipText, direction === value && styles.chipTextSelected]}>
                {value === "in" ? "Giriş" : "Çıkış"}
              </Text>
            </TouchableOpacity>
          ))}
        </View>
        <Text style={styles.fieldLabel}>Gerekçe</Text>
        <TextInput
          testID="punch-reason"
          style={[styles.input, styles.textArea]}
          value={reason}
          onChangeText={setReason}
          placeholder="Kaydın neden eksik olduğunu açıklayın"
          multiline
          numberOfLines={3}
        />
        <TouchableOpacity
          testID="punch-submit"
          style={[styles.submitButton, submitting && styles.submitButtonDisabled]}
          onPress={() => void submit()}
          disabled={submitting}
        >
          {submitting ? (
            <ActivityIndicator color={colors.white} />
          ) : (
            <Text style={styles.submitButtonText}>Talebi gönder</Text>
          )}
        </TouchableOpacity>
      </View>

      <Text style={styles.sectionTitle}>Taleplerim</Text>
      {requests.length === 0 ? (
        <Text style={styles.emptyText}>Henüz talebiniz yok.</Text>
      ) : (
        requests.map((request) => (
          <View key={request.id} style={styles.card}>
            <View style={styles.rowBetween}>
              <Text style={styles.cardValue}>
                {formatOccurred(request.occurredAtUtc)} · {request.direction === "in" ? "Giriş" : "Çıkış"}
              </Text>
              <Text
                testID={`punch-status-${request.id}`}
                style={[styles.statusChip, { color: STATUS_COLOR[request.status] }]}
              >
                {PUNCH_STATUS_LABEL[request.status]}
              </Text>
            </View>
            <Text style={styles.cardSubtext}>{request.reason}</Text>
            {canCancelPunchRequest(request.status) && (
              <TouchableOpacity
                testID={`punch-cancel-${request.id}`}
                style={styles.cancelButton}
                onPress={() => void cancel(request)}
              >
                <Text style={styles.cancelText}>İptal et</Text>
              </TouchableOpacity>
            )}
          </View>
        ))
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.xl, paddingBottom: spacing.xxxl },
  centered: { flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: colors.background },
  title: { ...typography.h3, color: colors.text, marginBottom: spacing.lg },
  sectionTitle: { ...typography.h4, color: colors.text, marginTop: spacing.xl, marginBottom: spacing.sm },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardValue: { ...typography.bodySm, color: colors.text, flexShrink: 1 },
  cardSubtext: { ...typography.bodyXs, color: colors.textTertiary, marginTop: spacing.xs },
  emptyText: { ...typography.bodySm, color: colors.textTertiary },
  errorBanner: { backgroundColor: "#fdecea", borderRadius: 8, padding: spacing.md, marginBottom: spacing.md },
  errorText: { ...typography.bodySm, color: colors.error },
  fieldLabel: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.md, marginBottom: spacing.xs },
  row: { flexDirection: "row" },
  rowBetween: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  chip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    marginRight: spacing.sm,
    backgroundColor: colors.gray50,
  },
  chipSelected: { backgroundColor: colors.primary, borderColor: colors.primary },
  chipText: { ...typography.bodySm, color: colors.text },
  chipTextSelected: { color: colors.white },
  statusChip: { ...typography.caption, marginLeft: spacing.sm },
  input: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    backgroundColor: colors.white,
  },
  textArea: { minHeight: 72, textAlignVertical: "top" },
  submitButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.md,
    alignItems: "center",
    marginTop: spacing.lg,
  },
  submitButtonDisabled: { opacity: 0.6 },
  submitButtonText: { ...typography.body, color: colors.white },
  cancelButton: { marginTop: spacing.sm, alignSelf: "flex-start" },
  cancelText: { ...typography.bodySm, color: colors.error },
});
