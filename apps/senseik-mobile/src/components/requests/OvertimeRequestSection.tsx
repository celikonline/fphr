import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
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
  leaveService,
  OVERTIME_DEFAULT_PAY_RATE_KIND,
  type OvertimePayRateKind,
  type OvertimeRequest,
} from "@/services/LeaveService";
import { DateField } from "@/components/form/DateField";
import { TimeField } from "@/components/form/TimeField";
import { formatDurationMinutes, toLocalDateOnly } from "@/lib/formatters";
import {
  formatLeaveDateOnly,
  LEAVE_STATUS_COLORS,
  LEAVE_STATUS_LABELS,
} from "@/components/leave/LeaveRequestListItem";
import { isOvertimeDurationValid, parseAmountInput } from "@/lib/requestValidation";
import { overtimeWarningLabel } from "@/lib/overtimeWarnings";
import { getApiErrorMessage } from "@/lib/apiError";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/**
 * TK-031 (web parity): labels mirror `web/public/locales/tr/leave.json`'s `overtime.payRate.*`
 * (4857 md.41 - `OvertimePayRateKind` doc comment in `LeaveService.ts` has the legal detail).
 */
export const OVERTIME_PAY_RATE_LABELS: Record<OvertimePayRateKind, string> = {
  rate50: "%50 fazlası (fazla mesai)",
  rate25: "%25 fazlası (fazla sürelerle çalışma)",
};

/** `SubmitMyOvertimeRequestValidator`: duration must be `1..OvertimeLimits.MaxDurationMinutes`. */
export function canSubmitOvertimeRequest(params: { durationText: string }): boolean {
  const minutes = parseAmountInput(params.durationText);
  return minutes !== null && isOvertimeDurationValid(minutes);
}

export default function OvertimeRequestSection() {
  const [requests, setRequests] = useState<OvertimeRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [withdrawingId, setWithdrawingId] = useState<string | null>(null);

  const [workDate, setWorkDate] = useState(todayDateOnly());
  const [startTime, setStartTime] = useState("18:00");
  const [durationText, setDurationText] = useState("");
  const [description, setDescription] = useState("");
  const [payRateKind, setPayRateKind] = useState<OvertimePayRateKind>(
    OVERTIME_DEFAULT_PAY_RATE_KIND
  );

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      setRequests(await leaveService.getMyOvertimeRequests());
    } catch {
      setError("Fazla mesai talepleri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function submit() {
    const durationMinutes = parseAmountInput(durationText);
    if (durationMinutes === null || !isOvertimeDurationValid(durationMinutes)) {
      Alert.alert("Hata", "Lütfen 1 dakika ile 24 saat arasında geçerli bir süre girin.");
      return;
    }

    setSubmitting(true);
    try {
      const result = await leaveService.submitOvertimeRequest({
        workDate,
        startTime,
        durationMinutes,
        description: description.trim().length > 0 ? description.trim() : null,
        payRateKind,
      });
      setDurationText("");
      setDescription("");
      setPayRateKind(OVERTIME_DEFAULT_PAY_RATE_KIND);
      if (result.warnings.length > 0) {
        Alert.alert("Talep gönderildi", result.warnings.map(overtimeWarningLabel).join("\n\n"));
      }
      await loadAll();
    } catch (submitError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(submitError, "Fazla mesai talebi gönderilemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function withdraw(id: string) {
    setWithdrawingId(id);
    try {
      await leaveService.withdrawOvertimeRequest(id);
      await loadAll();
    } catch (withdrawError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(
          withdrawError,
          "Fazla mesai talebi geri çekilemedi. Lütfen tekrar deneyin."
        )
      );
    } finally {
      setWithdrawingId(null);
    }
  }

  function confirmWithdraw(id: string) {
    Alert.alert(
      "Fazla mesai talebini geri çek",
      "Bu talebi geri çekmek istediğinize emin misiniz? Bu işlem geri alınamaz.",
      [
        { text: "Vazgeç", style: "cancel" },
        { text: "Geri çek", style: "destructive", onPress: () => void withdraw(id) },
      ]
    );
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  const durationPreview = parseAmountInput(durationText);
  const canSubmit = canSubmitOvertimeRequest({ durationText }) && !submitting;

  return (
    <View>
      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <Text style={styles.fieldLabel}>Tarih</Text>
      <DateField value={workDate} onChange={setWorkDate} />

      <Text style={styles.fieldLabel}>Başlangıç saati</Text>
      <TimeField value={startTime} onChange={setStartTime} />

      <Text style={styles.fieldLabel}>Süre (dakika)</Text>
      <TextInput
        style={styles.input}
        value={durationText}
        onChangeText={setDurationText}
        placeholder="Örn. 120"
        keyboardType="number-pad"
      />
      {durationPreview !== null && isOvertimeDurationValid(durationPreview) && (
        <Text style={styles.previewText}>{formatDurationMinutes(durationPreview)}</Text>
      )}

      <Text style={styles.fieldLabel}>Ücret oranı</Text>
      <View style={styles.payRateRow}>
        {(Object.keys(OVERTIME_PAY_RATE_LABELS) as OvertimePayRateKind[]).map((kind) => {
          const selected = kind === payRateKind;
          return (
            <TouchableOpacity
              key={kind}
              style={[styles.payRateChip, selected && styles.payRateChipSelected]}
              onPress={() => setPayRateKind(kind)}
            >
              <Text style={[styles.payRateChipText, selected && styles.payRateChipTextSelected]}>
                {OVERTIME_PAY_RATE_LABELS[kind]}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>

      <Text style={styles.fieldLabel}>Açıklama (opsiyonel)</Text>
      <TextInput
        style={[styles.input, styles.textArea]}
        value={description}
        onChangeText={setDescription}
        placeholder="Örn. ay sonu kapanış çalışması"
        multiline
        numberOfLines={3}
      />

      <TouchableOpacity
        style={[styles.submitButton, !canSubmit && styles.submitButtonDisabled]}
        onPress={() => void submit()}
        disabled={!canSubmit}
      >
        {submitting ? (
          <ActivityIndicator color={colors.white} />
        ) : (
          <Text style={styles.submitButtonText}>Gönder</Text>
        )}
      </TouchableOpacity>

      <Text style={styles.sectionTitle}>Geçmiş fazla mesai taleplerim</Text>
      {requests.length === 0 ? (
        <Text style={styles.emptyText}>Henüz fazla mesai talebiniz yok.</Text>
      ) : (
        requests.map((request) => {
          const isWithdrawing = withdrawingId === request.id;
          return (
            <View key={request.id} style={styles.card}>
              <View style={styles.cardHeader}>
                <Text style={styles.cardSubtext}>
                  {formatLeaveDateOnly(request.workDate)} · {request.startTime.slice(0, 5)} ·{" "}
                  {formatDurationMinutes(request.durationMinutes)}
                </Text>
                <View
                  style={[
                    styles.statusBadge,
                    { backgroundColor: LEAVE_STATUS_COLORS[request.status] },
                  ]}
                >
                  <Text style={styles.statusBadgeText}>{LEAVE_STATUS_LABELS[request.status]}</Text>
                </View>
              </View>
              {request.description && <Text style={styles.cardSubtext}>{request.description}</Text>}
              {request.status === "pending" && (
                <TouchableOpacity
                  style={styles.withdrawButton}
                  onPress={() => confirmWithdraw(request.id)}
                  disabled={isWithdrawing}
                >
                  {isWithdrawing ? (
                    <ActivityIndicator size="small" color={colors.error} />
                  ) : (
                    <Text style={styles.withdrawButtonText}>Geri çek</Text>
                  )}
                </TouchableOpacity>
              )}
            </View>
          );
        })
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  centered: {
    paddingVertical: spacing.xxxl,
    alignItems: "center",
    justifyContent: "center",
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
  },
  payRateRow: {
    flexDirection: "row",
    flexWrap: "wrap",
  },
  payRateChip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    marginRight: spacing.sm,
    marginBottom: spacing.sm,
    backgroundColor: colors.gray50,
  },
  payRateChipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  payRateChipText: {
    ...typography.bodySm,
    color: colors.text,
  },
  payRateChipTextSelected: {
    color: colors.white,
  },
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
  textArea: {
    minHeight: 72,
    textAlignVertical: "top",
  },
  previewText: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: spacing.xs,
  },
  submitButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.md,
    alignItems: "center",
    marginTop: spacing.lg,
  },
  submitButtonDisabled: {
    opacity: 0.6,
  },
  submitButtonText: {
    ...typography.button,
    color: colors.white,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  errorBanner: {
    backgroundColor: "#fdecea",
    borderRadius: 8,
    padding: spacing.md,
    marginBottom: spacing.md,
  },
  errorText: {
    ...typography.bodySm,
    color: colors.error,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  statusBadge: {
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  statusBadgeText: {
    ...typography.caption,
    color: colors.white,
  },
  withdrawButton: {
    alignSelf: "flex-start",
    marginTop: spacing.sm,
    paddingVertical: spacing.xs,
  },
  withdrawButtonText: {
    ...typography.bodyXs,
    color: colors.error,
    fontWeight: "600",
  },
});
