import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
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
  shiftService,
  type AvailabilityKind,
  type EmployeeAvailability,
  type ShiftAssignment,
} from "@/services/ShiftService";
import { toLocalDateOnly } from "@/lib/formatters";

/** First and last `DateOnly` ("yyyy-MM-dd") of the week containing `reference` (Monday start). */
function weekRange(reference: Date): { from: string; to: string } {
  const day = reference.getDay();
  const mondayOffset = day === 0 ? -6 : 1 - day;
  const monday = new Date(reference);
  monday.setDate(reference.getDate() + mondayOffset);
  const sunday = new Date(monday);
  sunday.setDate(monday.getDate() + 6);
  return { from: toLocalDateOnly(monday), to: toLocalDateOnly(sunday) };
}

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

function formatDayLabel(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  const label = date.toLocaleDateString("tr-TR", {
    weekday: "short",
    day: "2-digit",
    month: "short",
  });
  return label.charAt(0).toUpperCase() + label.slice(1);
}

/** `TimeOnly` ("HH:mm:ss") → "HH:mm" for display. */
function formatTime(value: string | null | undefined): string {
  if (!value) return "—";
  return value.slice(0, 5);
}

/** Short, stable label for a colleague's shift row - the API does not return employee names on this DTO (SORU). */
function shortEmployeeLabel(employeeId: string): string {
  return `Çalışan #${employeeId.slice(0, 8)}`;
}

const AVAILABILITY_KIND_LABELS: Record<AvailabilityKind, string> = {
  unavailable: "Bugün çalışmak istemiyorum",
  weeklyRestRequest: "Haftalık iznimi kullanmak istiyorum",
};

export default function ShiftScreen() {
  const [weekDate, setWeekDate] = useState(() => new Date());
  const [myShifts, setMyShifts] = useState<ShiftAssignment[]>([]);
  const [teamShifts, setTeamShifts] = useState<ShiftAssignment[]>([]);
  const [teamShiftsVisible, setTeamShiftsVisible] = useState(true);
  const [myAvailability, setMyAvailability] = useState<EmployeeAvailability[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [availabilityDate, setAvailabilityDate] = useState(todayDateOnly());
  const [availabilityKind, setAvailabilityKind] = useState<AvailabilityKind>("unavailable");
  const [availabilityNote, setAvailabilityNote] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const loadAll = useCallback(async () => {
    setError(null);
    const range = weekRange(weekDate);
    try {
      const [mineResult, availabilityResult] = await Promise.all([
        shiftService.getMyShifts(range),
        shiftService.getMyAvailability(range),
      ]);
      setMyShifts([...mineResult].sort((a, b) => a.date.localeCompare(b.date)));
      setMyAvailability(availabilityResult.filter((entry) => entry.status === "active"));
    } catch {
      setError("Vardiya bilgileri yüklenemedi. Lütfen tekrar deneyin.");
    }

    // Ayrı try/catch: "attendance.shift.view" yetkisi olmayan bir kullanıcı için 403 gelir - bu,
    // kendi vardiyalarının/müsaitlik listesinin yüklenmesini engellememeli, bölüm sessizce gizlenir.
    try {
      const teamResult = await shiftService.getTeamShifts(range);
      setTeamShifts(
        [...teamResult]
          .filter((shift) => shift.status === "published")
          .sort((a, b) => a.date.localeCompare(b.date) || a.employeeId.localeCompare(b.employeeId))
      );
      setTeamShiftsVisible(true);
    } catch {
      setTeamShifts([]);
      setTeamShiftsVisible(false);
    }
  }, [weekDate]);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  function goToPreviousWeek() {
    setWeekDate((current) => {
      const next = new Date(current);
      next.setDate(current.getDate() - 7);
      return next;
    });
  }

  function goToNextWeek() {
    setWeekDate((current) => {
      const next = new Date(current);
      next.setDate(current.getDate() + 7);
      return next;
    });
  }

  async function submitAvailability() {
    if (!availabilityDate) {
      Alert.alert("Eksik bilgi", "Lütfen bir tarih girin.");
      return;
    }

    setSubmitting(true);
    try {
      await shiftService.submitMyAvailability({
        date: availabilityDate,
        kind: availabilityKind,
        note: availabilityNote.trim().length > 0 ? availabilityNote.trim() : null,
      });
      setAvailabilityNote("");
      await loadAll();
      Alert.alert("Kaydedildi", "Müsaitlik durumunuz kaydedildi.");
    } catch {
      Alert.alert("Hata", "Müsaitlik durumu kaydedilemedi. Lütfen tekrar deneyin.");
    } finally {
      setSubmitting(false);
    }
  }

  async function cancelAvailability(id: string) {
    try {
      await shiftService.cancelMyAvailability(id);
      await loadAll();
    } catch {
      Alert.alert("Hata", "Kayıt iptal edilemedi. Lütfen tekrar deneyin.");
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  const weekLabel = (() => {
    const { from, to } = weekRange(weekDate);
    return `${formatDayLabel(from)} – ${formatDayLabel(to)}`;
  })();

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>Vardiyalarım</Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <View style={styles.weekSelector}>
        <TouchableOpacity style={styles.weekButton} onPress={goToPreviousWeek}>
          <Text style={styles.weekButtonText}>‹</Text>
        </TouchableOpacity>
        <Text style={styles.weekLabel}>{weekLabel}</Text>
        <TouchableOpacity style={styles.weekButton} onPress={goToNextWeek}>
          <Text style={styles.weekButtonText}>›</Text>
        </TouchableOpacity>
      </View>

      <Text style={styles.sectionTitle}>Bu haftaki vardiyalarım</Text>
      {myShifts.length === 0 ? (
        <Text style={styles.emptyText}>Bu hafta için yayınlanmış vardiyanız yok.</Text>
      ) : (
        myShifts.map((shift) => (
          <View key={shift.id} style={styles.card}>
            <View style={styles.rowHeader}>
              <Text style={styles.cardLabel}>{formatDayLabel(shift.date)}</Text>
              {shift.isNight && (
                <View style={styles.nightBadge}>
                  <Text style={styles.nightBadgeText}>Gece</Text>
                </View>
              )}
            </View>
            <Text style={styles.cardValue}>
              {formatTime(shift.start)} – {formatTime(shift.end)}
            </Text>
            {shift.note && <Text style={styles.cardSubtext}>{shift.note}</Text>}
          </View>
        ))
      )}

      {teamShiftsVisible && (
        <>
          <Text style={styles.sectionTitle}>Ekip vardiyaları</Text>
          {teamShifts.length === 0 ? (
            <Text style={styles.emptyText}>
              Bu hafta için görüntüleyebileceğiniz ekip vardiyası yok.
            </Text>
          ) : (
            teamShifts.map((shift) => (
              <View key={shift.id} style={styles.card}>
                <View style={styles.rowHeader}>
                  <Text style={styles.cardLabel}>
                    {formatDayLabel(shift.date)} · {shortEmployeeLabel(shift.employeeId)}
                  </Text>
                  {shift.isNight && (
                    <View style={styles.nightBadge}>
                      <Text style={styles.nightBadgeText}>Gece</Text>
                    </View>
                  )}
                </View>
                <Text style={styles.cardValue}>
                  {formatTime(shift.start)} – {formatTime(shift.end)}
                </Text>
              </View>
            ))
          )}
        </>
      )}

      <Text style={styles.sectionTitle}>Müsait değilim</Text>
      <View style={styles.card}>
        <Text style={styles.fieldLabel}>Tarih (yyyy-aa-gg)</Text>
        <TextInput
          style={styles.input}
          value={availabilityDate}
          onChangeText={setAvailabilityDate}
          placeholder="2026-09-25"
          autoCapitalize="none"
        />

        <Text style={styles.fieldLabel}>Tür</Text>
        <View style={styles.chipRow}>
          {(Object.keys(AVAILABILITY_KIND_LABELS) as AvailabilityKind[]).map((kind) => {
            const selected = kind === availabilityKind;
            return (
              <TouchableOpacity
                key={kind}
                style={[styles.chip, selected && styles.chipSelected]}
                onPress={() => setAvailabilityKind(kind)}
              >
                <Text style={[styles.chipText, selected && styles.chipTextSelected]}>
                  {AVAILABILITY_KIND_LABELS[kind]}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        <Text style={styles.fieldLabel}>Not (opsiyonel)</Text>
        <TextInput
          style={[styles.input, styles.textArea]}
          value={availabilityNote}
          onChangeText={setAvailabilityNote}
          placeholder="Ek bilgi ekleyin"
          multiline
          numberOfLines={3}
        />

        <TouchableOpacity
          style={[styles.submitButton, submitting && styles.submitButtonDisabled]}
          onPress={() => void submitAvailability()}
          disabled={submitting}
        >
          {submitting ? (
            <ActivityIndicator color={colors.white} />
          ) : (
            <Text style={styles.submitButtonText}>Kaydet</Text>
          )}
        </TouchableOpacity>
      </View>

      {myAvailability.length > 0 && (
        <>
          <Text style={styles.sectionTitle}>Müsaitlik kayıtlarım</Text>
          {myAvailability.map((entry) => (
            <View key={entry.id} style={styles.card}>
              <View style={styles.rowHeader}>
                <Text style={styles.cardLabel}>{formatDayLabel(entry.date)}</Text>
                <TouchableOpacity onPress={() => void cancelAvailability(entry.id)}>
                  <Text style={styles.cancelLink}>İptal et</Text>
                </TouchableOpacity>
              </View>
              <Text style={styles.cardValue}>{AVAILABILITY_KIND_LABELS[entry.kind]}</Text>
              <Text style={styles.cardSubtext}>
                {entry.start && entry.end
                  ? `${formatTime(entry.start)} – ${formatTime(entry.end)}`
                  : "Tüm gün"}
              </Text>
              {entry.note && <Text style={styles.cardSubtext}>{entry.note}</Text>}
            </View>
          ))}
        </>
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
  },
  weekSelector: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: colors.gray50,
    borderRadius: 12,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    marginBottom: spacing.md,
  },
  weekButton: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  weekButtonText: {
    ...typography.h4,
    color: colors.primary,
  },
  weekLabel: {
    ...typography.body,
    color: colors.text,
    fontWeight: "600",
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardLabel: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  cardValue: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xs,
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
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
  rowHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  nightBadge: {
    backgroundColor: colors.gray700,
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  nightBadgeText: {
    ...typography.caption,
    color: colors.white,
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  chipRow: {
    flexDirection: "row",
    flexWrap: "wrap",
  },
  chip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    marginRight: spacing.sm,
    marginBottom: spacing.sm,
    backgroundColor: colors.gray50,
  },
  chipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  chipText: {
    ...typography.bodySm,
    color: colors.text,
  },
  chipTextSelected: {
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
  cancelLink: {
    ...typography.bodyXs,
    color: colors.error,
  },
});
