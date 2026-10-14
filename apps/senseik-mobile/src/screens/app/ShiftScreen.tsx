import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, Alert, ScrollView, StyleSheet, View } from "react-native";
import { useTranslation } from "react-i18next";
import * as Sharing from "expo-sharing";
import i18n from "@/lib/i18n";
import {
  EmptyState,
  ErrorBanner,
  PrimaryButton,
  Screen,
  ScreenHeader,
  SectionHeader,
} from "@/components/ui";
import {
  shiftService,
  type AvailabilityKind,
  type EmployeeAvailability,
  type ShiftAssignment,
} from "@/services/ShiftService";
import { createShiftExport, deleteCachedShiftExport } from "@/services/ShiftExportService";
import { toLocalDateOnly } from "@/lib/formatters";
import { formatMonthYear, shiftMonth, toDateOnly } from "@/components/form/dateRange";
import { palette } from "@/theme/theme";
import { PeriodStepper } from "./attendance/PeriodStepper";
import { formatTimeOfDay } from "./attendance/attendanceStatus";
import { AvailabilityEntryCard } from "./shift/AvailabilityEntryCard";
import { AvailabilityForm } from "./shift/AvailabilityForm";
import { ShiftCard } from "./shift/ShiftCard";
import { MonthCalendar } from "./shift/MonthCalendar";
import { formatDayLabel } from "./shift/shiftWeek";

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
 * Vardiyam: monthly personal calendar (tap a day to narrow the lists), own and team
 * shifts as glass cards, and the "Müsait değilim" form with the active availability records.
 */
export default function ShiftScreen() {
  const { t } = useTranslation("shift");
  const scrollViewRef = useRef<ScrollView>(null);
  const [availabilityFormOffset, setAvailabilityFormOffset] = useState(0);
  const [monthDate, setMonthDate] = useState(() => new Date());
  const [selectedDay, setSelectedDay] = useState<string | null>(() => todayDateOnly());
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
  const [exporting, setExporting] = useState(false);

  const loadAll = useCallback(async () => {
    setError(null);
    const year = monthDate.getFullYear();
    const month0 = monthDate.getMonth();
    const range = {
      from: toDateOnly(year, month0, 1),
      to: toDateOnly(year, month0, new Date(year, month0 + 1, 0).getDate()),
    };
    try {
      const [mineResult, availabilityResult] = await Promise.all([
        shiftService.getMyShifts(range),
        shiftService.getMyAvailability(range),
      ]);
      setMyShifts([...mineResult].sort((a, b) => a.date.localeCompare(b.date)));
      setMyAvailability(availabilityResult.filter((entry) => entry.status === "active"));
    } catch {
      setMyShifts([]);
      setMyAvailability([]);
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
  }, [monthDate]);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  function goToPreviousMonth() {
    setSelectedDay(null);
    setMonthDate((current) => {
      const next = shiftMonth(current.getFullYear(), current.getMonth(), -1);
      return new Date(next.year, next.month0, 1);
    });
  }

  function goToNextMonth() {
    setSelectedDay(null);
    setMonthDate((current) => {
      const next = shiftMonth(current.getFullYear(), current.getMonth(), 1);
      return new Date(next.year, next.month0, 1);
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

  async function exportMonth() {
    if (myShifts.length === 0) return;
    setExporting(true);
    let fileUri: string | null = null;
    try {
      if (!(await Sharing.isAvailableAsync())) {
        Alert.alert(t("common:alerts.error"), t("export.unavailable"));
        return;
      }
      fileUri = createShiftExport(myShifts, monthDate.getFullYear(), monthDate.getMonth(), {
        date: t("export.columns.date"),
        start: t("export.columns.start"),
        end: t("export.columns.end"),
        breakMinutes: t("export.columns.breakMinutes"),
        night: t("export.columns.night"),
        note: t("export.columns.note"),
        sheetName: t("export.sheetName"),
        yes: t("export.columns.yes"),
        no: t("export.columns.no"),
      });
      await Sharing.shareAsync(fileUri, {
        mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        dialogTitle: t("export.title"),
        UTI: "org.openxmlformats.spreadsheetml.sheet",
      });
    } catch {
      Alert.alert(t("common:alerts.error"), t("export.error"));
    } finally {
      if (fileUri) deleteCachedShiftExport(fileUri);
      setExporting(false);
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

  const onSelectedDay = (shift: ShiftAssignment) => !selectedDay || shift.date === selectedDay;
  const visibleMine = myShifts.filter(onSelectedDay);
  const visibleTeam = teamShifts.filter(onSelectedDay);

  return (
    <Screen
      header={header}
      refreshing={refreshing}
      onRefresh={() => void onRefresh()}
      scrollViewRef={scrollViewRef}
    >
      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      <View style={styles.period}>
        <PeriodStepper
          label={formatMonthYear(monthDate.getFullYear(), monthDate.getMonth())}
          onPrev={goToPreviousMonth}
          onNext={goToNextMonth}
          prevLabel={t("previousMonth")}
          nextLabel={t("nextMonth")}
        />
      </View>
      <MonthCalendar
        year={monthDate.getFullYear()}
        month0={monthDate.getMonth()}
        today={todayDateOnly()}
        selected={selectedDay}
        markedDays={new Set(myShifts.map((shift) => shift.date))}
        shiftLabel={t("calendar.shift")}
        onSelect={(day) => setSelectedDay((current) => (current === day ? null : day))}
      />

      <View style={styles.exportButton}>
        <PrimaryButton
          label={t("export.button")}
          icon="download-outline"
          onPress={() => void exportMonth()}
          loading={exporting}
          disabled={myShifts.length === 0}
          variant="secondary"
        />
      </View>

      <SectionHeader title={selectedDay ? formatDayLabel(selectedDay) : t("sections.myMonth")} />
      {visibleMine.length === 0 ? (
        <EmptyState
          icon="calendar-outline"
          title={t(selectedDay ? "empty.day.mine" : "empty.month.mine")}
          message={t("empty.mineGuide")}
          actionLabel={t(myShifts.length > 0 ? "empty.showMonth" : "empty.goToAvailability")}
          onAction={() => {
            if (myShifts.length > 0) {
              setSelectedDay(null);
            } else {
              setAvailabilityDate(selectedDay ?? todayDateOnly());
              scrollViewRef.current?.scrollTo({ y: availabilityFormOffset, animated: true });
            }
          }}
        />
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
            <EmptyState
              icon="people-outline"
              title={t(selectedDay ? "empty.day.team" : "empty.month.team")}
              message={t("empty.teamGuide")}
              actionLabel={
                selectedDay && teamShifts.length > 0
                  ? t("empty.showMonth")
                  : t("empty.changeMonth")
              }
              onAction={() =>
                selectedDay && teamShifts.length > 0
                  ? setSelectedDay(null)
                  : scrollViewRef.current?.scrollTo({ y: 0, animated: true })
              }
            />
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
      <View onLayout={(event) => setAvailabilityFormOffset(event.nativeEvent.layout.y)}>
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
      </View>

      <SectionHeader title={t("sections.myAvailability")} />
      {myAvailability.length === 0 ? (
        <EmptyState
          icon="calendar-clear-outline"
          title={t("availability.empty.title")}
          message={t("availability.empty.message")}
          actionLabel={t("availability.empty.action")}
          onAction={() =>
            scrollViewRef.current?.scrollTo({ y: availabilityFormOffset, animated: true })
          }
        />
      ) : (
        <>
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
      )}
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
  exportButton: {
    paddingHorizontal: 20,
    paddingTop: 14,
  },
});
