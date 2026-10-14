import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  Alert,
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { useTranslation } from "react-i18next";
import { EmptyState } from "@/components/ui";
import { formatDateTime } from "@/lib/formatters";
import { palette, radius, text, tones } from "@/theme/theme";
import {
  canCancelPunchRequest,
  isFutureTime,
  parseLocalDateTime,
  punchRequestService,
  punchStatusLabel,
  type PunchRequest,
  type PunchRequestDirection,
  type PunchRequestStatus,
} from "@/services/PunchRequestService";

const STATUS_COLOR: Record<PunchRequestStatus, string> = {
  pending: tones.warning.fg,
  approved: tones.success.fg,
  rejected: tones.danger.fg,
  cancelled: palette.textFaint,
};

function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function errorMessage(error: unknown, fallback: string): string {
  const detail = (error as { response?: { data?: { detail?: string } } })?.response?.data?.detail;
  return detail || fallback;
}

function formatOccurred(value: string): string {
  return formatDateTime(new Date(value), { dateStyle: "medium", timeStyle: "short" });
}

const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 20, xxxl: 32 } as const;

/** Eksik kayıt talebi (Z2.1): form + kendi talepleri; yalnız bekleyen talep iptal edilir. */
export default function PunchRequestScreen() {
  const { t } = useTranslation("checkin");
  const scrollViewRef = useRef<ScrollView>(null);
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
      setError(t("punchRequest.loadError"));
    }
  }, [t]);

  useEffect(() => {
    void load().finally(() => setLoading(false));
  }, [load]);

  async function submit() {
    const occurredAt = parseLocalDateTime(date, time);
    if (!occurredAt) {
      Alert.alert(t("common:alerts.missingInfo"), t("punchRequest.invalidDateTime"));
      return;
    }
    if (isFutureTime(occurredAt)) {
      Alert.alert(t("punchRequest.invalidTimeTitle"), t("punchRequest.futureTime"));
      return;
    }
    if (reason.trim().length === 0) {
      Alert.alert(t("common:alerts.missingInfo"), t("punchRequest.reasonRequired"));
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
      Alert.alert(t("common:alerts.sent"), t("punchRequest.sentMessage"));
    } catch (err) {
      Alert.alert(t("common:alerts.error"), errorMessage(err, t("punchRequest.submitError")));
    } finally {
      setSubmitting(false);
    }
  }

  async function cancel(request: PunchRequest) {
    if (!canCancelPunchRequest(request.status)) return;
    try {
      await punchRequestService.cancel(request.id);
      await load();
    } catch (err) {
      Alert.alert(t("common:alerts.error"), errorMessage(err, t("punchRequest.cancelError")));
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={palette.brand} />
      </View>
    );
  }

  return (
    <ScrollView ref={scrollViewRef} style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>{t("punchRequest.title")}</Text>
      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <View style={styles.card}>
        <Text style={styles.fieldLabel}>{t("punchRequest.date")}</Text>
        <TextInput
          testID="punch-date"
          style={styles.input}
          value={date}
          onChangeText={setDate}
          autoCapitalize="none"
          placeholderTextColor={palette.textFaint}
          placeholder="2026-09-20"
        />
        <Text style={styles.fieldLabel}>{t("punchRequest.time")}</Text>
        <TextInput
          testID="punch-time"
          style={styles.input}
          value={time}
          onChangeText={setTime}
          autoCapitalize="none"
          placeholderTextColor={palette.textFaint}
          placeholder="08:30"
        />
        <Text style={styles.fieldLabel}>{t("punchRequest.direction")}</Text>
        <View style={styles.row}>
          {(["in", "out"] as const).map((value) => (
            <TouchableOpacity
              key={value}
              testID={`punch-direction-${value}`}
              style={[styles.chip, direction === value && styles.chipSelected]}
              onPress={() => setDirection(value)}
            >
              <Text style={[styles.chipText, direction === value && styles.chipTextSelected]}>
                {t(value === "in" ? "punchRequest.in" : "punchRequest.out")}
              </Text>
            </TouchableOpacity>
          ))}
        </View>
        <Text style={styles.fieldLabel}>{t("punchRequest.reason")}</Text>
        <TextInput
          testID="punch-reason"
          style={[styles.input, styles.textArea]}
          value={reason}
          onChangeText={setReason}
          placeholderTextColor={palette.textFaint}
          placeholder={t("punchRequest.reasonPlaceholder")}
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
            <ActivityIndicator color={palette.white} />
          ) : (
            <Text style={styles.submitButtonText}>{t("punchRequest.submit")}</Text>
          )}
        </TouchableOpacity>
      </View>

      <Text style={styles.sectionTitle}>{t("punchRequest.mine")}</Text>
      {error ? null : requests.length === 0 ? (
        <EmptyState
          icon="time-outline"
          title={t("punchRequest.empty")}
          message={t("punchRequest.emptyGuide")}
          actionLabel={t("punchRequest.createAnother")}
          onAction={() => scrollViewRef.current?.scrollTo({ y: 0, animated: true })}
        />
      ) : (
        requests.map((request) => (
          <View key={request.id} style={styles.card}>
            <View style={styles.rowBetween}>
              <Text style={styles.cardValue}>
                {formatOccurred(request.occurredAtUtc)} ·{" "}
                {t(request.direction === "in" ? "punchRequest.in" : "punchRequest.out")}
              </Text>
              <Text
                testID={`punch-status-${request.id}`}
                style={[styles.statusChip, { color: STATUS_COLOR[request.status] }]}
              >
                {punchStatusLabel(request.status)}
              </Text>
            </View>
            {request.decidedAtUtc ? (
              <Text testID={`punch-decided-${request.id}`} style={styles.cardSubtext}>
                {t("punchRequest.decidedAt", { time: formatOccurred(request.decidedAtUtc) })}
              </Text>
            ) : null}
            <Text style={styles.cardSubtext}>{request.reason}</Text>
            {canCancelPunchRequest(request.status) && (
              <TouchableOpacity
                testID={`punch-cancel-${request.id}`}
                style={styles.cancelButton}
                onPress={() => void cancel(request)}
              >
                <Text style={styles.cancelText}>{t("punchRequest.cancel")}</Text>
              </TouchableOpacity>
            )}
          </View>
        ))
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: palette.night },
  content: { padding: spacing.xl, paddingBottom: spacing.xxxl },
  centered: { flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: palette.night },
  title: { ...text.title, color: palette.white, marginBottom: spacing.lg },
  sectionTitle: { ...text.heading, color: palette.white, marginTop: spacing.xl, marginBottom: spacing.sm },
  card: {
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardValue: { ...text.bodyMuted, color: palette.white, flexShrink: 1 },
  cardSubtext: { ...text.caption, color: palette.textFaint, marginTop: spacing.xs },
  errorBanner: { backgroundColor: tones.danger.bg, borderRadius: radius.button, padding: spacing.md, marginBottom: spacing.md },
  errorText: { ...text.bodyMuted, color: tones.danger.fg },
  fieldLabel: { ...text.caption, color: palette.textMuted, marginTop: spacing.md, marginBottom: spacing.xs },
  row: { flexDirection: "row" },
  rowBetween: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  chip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    marginRight: spacing.sm,
    backgroundColor: palette.glassInner,
  },
  chipSelected: { backgroundColor: palette.brand, borderColor: palette.brand },
  chipText: { ...text.bodyMuted, color: palette.white },
  chipTextSelected: { color: palette.white },
  statusChip: { ...text.caption, marginLeft: spacing.sm },
  input: {
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...text.body,
    color: palette.white,
    backgroundColor: palette.glass,
  },
  textArea: { minHeight: 72, textAlignVertical: "top" },
  submitButton: {
    backgroundColor: palette.brand,
    borderRadius: radius.button,
    paddingVertical: spacing.md,
    alignItems: "center",
    marginTop: spacing.lg,
  },
  submitButtonDisabled: { opacity: 0.6 },
  submitButtonText: { ...text.body, color: palette.white },
  cancelButton: { marginTop: spacing.sm, alignSelf: "flex-start" },
  cancelText: { ...text.bodyMuted, color: tones.danger.fg },
});
