import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { ErrorBanner, Screen, ScreenHeader, SectionHeader } from "@/components/ui";
import {
  shiftService,
  type AvailabilityKind,
  type EmployeeAvailability,
  type ShiftAssignment,
} from "@/services/ShiftService";
import { toLocalDateOnly } from "@/lib/formatters";
import { palette, text } from "@/theme/theme";
import { PeriodStepper } from "./attendance/PeriodStepper";
import { formatTimeOfDay } from "./attendance/attendanceStatus";
import { AvailabilityEntryCard } from "./shift/AvailabilityEntryCard";
import { AvailabilityForm } from "./shift/AvailabilityForm";
import { ShiftCard } from "./shift/ShiftCard";
import { WeekStrip } from "./shift/WeekStrip";
import { emptyShiftTexts, formatDayLabel, formatWeekLabel, weekDays } from "./shift/shiftWeek";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

function formatTimeRange(start?: string | null, end?: string | null): string {
  return `${formatTimeOfDay(start)} – ${formatTimeOfDay(end)}`;
}

/** Short, stable label for a colleague's shift row - the API does not return employee names on this DTO (SORU). */
function shortEmployeeLabel(employeeId: string): string {
  return i18n.t("shift:employeeLabel", { id: employeeId.slice(0, 8) });
}

/**
 * Vardiyam (spec §5): week stepper + week strip (tap a day to narrow the lists), own and team
 * shifts as glass cards, and the "Müsait değilim" form with the active availability records.
 */
export default function ShiftScreen() {
  const { t } = useTranslation("shift");
  const [weekDate, setWeekDate] = useState(() => new Date());
  const [selectedDay, setSelectedDay] = useState<string | null>(null);
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
    const days = weekDays(weekDate);
    const range = { from: days[0], to: days[days.length - 1] };
    try {
      const [mineResult, availabilityResult] = await Promise.all([
        shiftService.getMyShifts(range),
        shiftService.getMyAvailability(range),
      ]);
      setMyShifts([...mineResult].sort((a, b) => a.date.localeCompare(b.date)));
      setMyAvailability(availabilityResult.filter((entry) => entry.status === "active"));
    } catch {
      setError(i18n.t("shift:loadError"));
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
    setSelectedDay(null);
    setWeekDate((current) => {
      const next = new Date(current);
      next.setDate(current.getDate() - 7);
      return next;
    });
  }

  function goToNextWeek() {
    setSelectedDay(null);
    setWeekDate((current) => {
      const next = new Date(current);
      next.setDate(current.getDate() + 7);
      return next;
    });
  }

  async function submitAvailability() {
    if (!availabilityDate) {
      Alert.alert(t("common:alerts.missingInfo"), t("availability.dateRequired"));
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
      Alert.alert(t("common:alerts.saved"), t("availability.saved"));
    } catch {
      Alert.alert(t("common:alerts.error"), t("availability.saveError"));
    } finally {
      setSubmitting(false);
    }
  }

  async function cancelAvailability(id: string) {
    try {
      await shiftService.cancelMyAvailability(id);
      await loadAll();
    } catch {
      Alert.alert(t("common:alerts.error"), t("availability.cancelError"));
    }
  }

  // B6: irreversible action - confirm before cancelling instead of acting on the first tap.
  function confirmCancelAvailability(id: string) {
    Alert.alert(t("availability.cancelConfirmTitle"), t("availability.cancelConfirmMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("availability.cancel"),
        style: "destructive",
        onPress: () => void cancelAvailability(id),
      },
    ]);
  }

  const header = <ScreenHeader title={t("title")} />;

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
      </Screen>
    );
  }

  const days = weekDays(weekDate);
  const onSelectedDay = (shift: ShiftAssignment) => !selectedDay || shift.date === selectedDay;
  const visibleMine = myShifts.filter(onSelectedDay);
  const visibleTeam = teamShifts.filter(onSelectedDay);
  const emptyTexts = emptyShiftTexts(selectedDay);

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      <View style={styles.period}>
        <PeriodStepper
          label={formatWeekLabel(days)}
          onPrev={goToPreviousWeek}
          onNext={goToNextWeek}
          prevLabel={t("previousWeek")}
          nextLabel={t("nextWeek")}
        />
      </View>
      <WeekStrip
        days={days}
        today={todayDateOnly()}
        selected={selectedDay}
        markedDays={new Set(myShifts.map((shift) => shift.date))}
        onSelect={(day) => setSelectedDay((current) => (current === day ? null : day))}
      />

      <SectionHeader title={selectedDay ? formatDayLabel(selectedDay) : t("sections.myWeek")} />
      {visibleMine.length === 0 ? (
        <Text style={styles.emptyText}>{emptyTexts.mine}</Text>
      ) : (
        visibleMine.map((shift) => (
          <ShiftCard
            key={shift.id}
            title={formatDayLabel(shift.date)}
            timeRange={formatTimeRange(shift.start, shift.end)}
            isNight={shift.isNight}
            note={shift.note}
          />
        ))
      )}

      {teamShiftsVisible ? (
        <>
          <SectionHeader title={t("sections.team")} />
          {visibleTeam.length === 0 ? (
            <Text style={styles.emptyText}>{emptyTexts.team}</Text>
          ) : (
            visibleTeam.map((shift) => (
              <ShiftCard
                key={shift.id}
                title={`${formatDayLabel(shift.date)} · ${shortEmployeeLabel(shift.employeeId)}`}
                timeRange={formatTimeRange(shift.start, shift.end)}
                isNight={shift.isNight}
              />
            ))
          )}
        </>
      ) : null}

      <SectionHeader title={t("sections.unavailable")} />
      <AvailabilityForm
        date={availabilityDate}
        onDateChange={setAvailabilityDate}
        kind={availabilityKind}
        onKindChange={setAvailabilityKind}
        note={availabilityNote}
        onNoteChange={setAvailabilityNote}
        submitting={submitting}
        onSubmit={() => void submitAvailability()}
      />

      {myAvailability.length > 0 ? (
        <>
          <SectionHeader title={t("sections.myAvailability")} />
          {myAvailability.map((entry) => (
            <AvailabilityEntryCard
              key={entry.id}
              entry={entry}
              timeRange={
                entry.start && entry.end ? formatTimeRange(entry.start, entry.end) : t("allDay")
              }
              onCancel={() => confirmCancelAvailability(entry.id)}
            />
          ))}
        </>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  period: {
    flexDirection: "row",
    paddingHorizontal: 20,
    paddingBottom: 12,
  },
  emptyText: {
    ...text.bodyMuted,
    paddingHorizontal: 20,
  },
});
