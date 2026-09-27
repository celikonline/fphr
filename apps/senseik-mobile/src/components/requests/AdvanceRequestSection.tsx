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
  expenseService,
  EXPENSE_DEFAULT_CURRENCY,
  type AdvanceRequest,
} from "@/services/ExpenseService";
import { DateField } from "@/components/form/DateField";
import { formatCurrencyTRY, toLocalDateOnly } from "@/lib/formatters";
import { formatLeaveDateOnly } from "@/components/leave/LeaveRequestListItem";
import {
  isAdvanceRequestedDateAllowed,
  isPositiveAmount,
  parseAmountInput,
} from "@/lib/requestValidation";
import { ADVANCE_STATUS_LABELS } from "@/lib/expenseLabels";
import { getApiErrorMessage } from "@/lib/apiError";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/** `CreateAdvanceRequestValidator`: only `Amount` (>0) and `RequestedDate` (when given) are checked client-side. */
export function canSubmitAdvanceRequest(params: {
  amountText: string;
  requestedDate: string;
}): boolean {
  const amount = parseAmountInput(params.amountText);
  if (amount === null || !isPositiveAmount(amount)) return false;
  return isAdvanceRequestedDateAllowed(params.requestedDate);
}

export default function AdvanceRequestSection() {
  const [requests, setRequests] = useState<AdvanceRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [amountText, setAmountText] = useState("");
  const [requestedDate, setRequestedDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      setRequests(await expenseService.getMyAdvanceRequests());
    } catch {
      setError("Avans talepleri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function submit() {
    const amount = parseAmountInput(amountText);
    if (amount === null || !isPositiveAmount(amount)) {
      Alert.alert("Hata", "Lütfen geçerli, sıfırdan büyük bir tutar girin.");
      return;
    }
    if (!isAdvanceRequestedDateAllowed(requestedDate)) {
      Alert.alert("Hata", "Talep tarihi izin verilen aralığın dışında.");
      return;
    }

    setSubmitting(true);
    try {
      await expenseService.createAdvanceRequest({
        amount,
        currency: EXPENSE_DEFAULT_CURRENCY,
        requestedDate,
        isInstallmentRequested: false,
        description: description.trim().length > 0 ? description.trim() : null,
      });
      setAmountText("");
      setDescription("");
      setRequestedDate(todayDateOnly());
      await loadAll();
    } catch (submitError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(submitError, "Avans talebi gönderilemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  const canSubmit = canSubmitAdvanceRequest({ amountText, requestedDate }) && !submitting;

  return (
    <View>
      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <Text style={styles.fieldLabel}>Tutar (TRY)</Text>
      <TextInput
        style={styles.input}
        value={amountText}
        onChangeText={setAmountText}
        placeholder="0,00"
        keyboardType="decimal-pad"
      />

      <Text style={styles.fieldLabel}>Talep tarihi</Text>
      <DateField value={requestedDate} onChange={setRequestedDate} />

      <Text style={styles.fieldLabel}>Açıklama (opsiyonel)</Text>
      <TextInput
        style={[styles.input, styles.textArea]}
        value={description}
        onChangeText={setDescription}
        placeholder="Örn. yol masrafı için avans"
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

      <Text style={styles.sectionTitle}>Geçmiş avans taleplerim</Text>
      {requests.length === 0 ? (
        <Text style={styles.emptyText}>Henüz avans talebiniz yok.</Text>
      ) : (
        requests.map((request) => (
          <View key={request.id} style={styles.card}>
            <View style={styles.cardHeader}>
              <Text style={styles.cardAmount}>{formatCurrencyTRY(request.amount)}</Text>
              <View style={styles.statusBadge}>
                <Text style={styles.statusBadgeText}>{ADVANCE_STATUS_LABELS[request.status]}</Text>
              </View>
            </View>
            <Text style={styles.cardSubtext}>{formatLeaveDateOnly(request.requestedDate)}</Text>
            {request.description && <Text style={styles.cardSubtext}>{request.description}</Text>}
          </View>
        ))
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
  cardAmount: {
    ...typography.h4,
    color: colors.text,
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
    backgroundColor: colors.gray100,
  },
  statusBadgeText: {
    ...typography.caption,
    color: colors.textSecondary,
  },
});
