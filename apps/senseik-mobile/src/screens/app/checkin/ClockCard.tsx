import React, { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { GlassCard, StatusPill } from "@/components/ui";
import {
  accuracyLabel,
  accuracyLevel,
  elapsedSinceCheckIn,
  formatClockDate,
  formatGmtOffset,
} from "@/lib/punch";
import { palette, text } from "@/theme/theme";
import { formatDistance, isInsideGeofence, type NearestLocation } from "./geofence";

const TICK_MS = 1000;

type Props = {
  /** Ticking pauses while false (tab not focused), so a background tab never re-renders every second. */
  active: boolean;
  /** Today's check-in ("HH:mm:ss") while still working; drives the "Girişten beri …" line. */
  workingSince: string | null;
  /** `expo-location` `coords.accuracy` in meters; `null` when unknown. */
  accuracyMeters: number | null;
  /** FG-005: the caller's check-in location (nearest), `null` for a roaming employee - no line, no pill. */
  location: NearestLocation | null;
};

function formatClock(date: Date): string {
  return [date.getHours(), date.getMinutes(), date.getSeconds()]
    .map((part) => String(part).padStart(2, "0"))
    .join(":");
}

/**
 * Live clock (HH:mm:ss, 1 s tick - a clock, not an animation, so reduced motion does not stop it),
 * GMT offset + date, location accuracy badge; then the "Girişten beri …" line below the card. The
 * tick lives here so only this card re-renders every second, not the map above it.
 */
export function ClockCard({ active, workingSince, accuracyMeters, location }: Props) {
  const { t } = useTranslation("checkin");
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    if (!active) return undefined;
    setNow(new Date());
    const timer = setInterval(() => setNow(new Date()), TICK_MS);
    return () => clearInterval(timer);
  }, [active]);

  const accuracy = accuracyLabel(accuracyMeters);
  const level = accuracyLevel(accuracyMeters);
  const elapsed = workingSince ? elapsedSinceCheckIn(workingSince, now) : null;
  const clock = formatClock(now);
  const inside = isInsideGeofence(location);
  const locationLine = location
    ? location.distanceMeters === null
      ? location.location.name
      : t("geofence.distance", {
          name: location.location.name,
          distance: formatDistance(location.distanceMeters),
        })
    : null;

  return (
    <>
      <GlassCard style={styles.card}>
        <Text
          style={styles.clock}
          accessibilityRole="text"
          accessibilityLabel={t("clock.a11y", { time: clock.slice(0, 5) })}
        >
          {clock}
        </Text>
        <Text style={styles.caption}>
          {formatGmtOffset(now)} · {formatClockDate(now)}
        </Text>
        <View style={styles.accuracyRow}>
          <Text style={styles.accuracyText}>
            {t("clock.accuracy", {
              value:
                accuracyMeters === null
                  ? t("clock.accuracyUnknown")
                  : t("clock.meters", { value: Math.round(accuracyMeters) }),
            })}
          </Text>
          {accuracy ? (
            <StatusPill label={accuracy} tone={level === "good" ? "success" : "warning"} />
          ) : null}
          {inside === null ? null : (
            <StatusPill
              label={inside ? t("geofence.inside") : t("geofence.outside")}
              tone={inside ? "success" : "danger"}
            />
          )}
        </View>
        {locationLine ? (
          <Text style={styles.location} numberOfLines={1}>
            {locationLine}
          </Text>
        ) : null}
      </GlassCard>
      {elapsed ? <Text style={styles.elapsed}>{t("clock.sinceCheckIn", { elapsed })}</Text> : null}
    </>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginTop: 14,
    alignItems: "center",
  },
  clock: {
    ...text.display,
    fontSize: 40,
    lineHeight: 48,
    fontVariant: ["tabular-nums"],
  },
  caption: {
    ...text.caption,
    marginTop: 2,
  },
  accuracyRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    marginTop: 12,
    paddingTop: 12,
    borderTopWidth: 1,
    borderTopColor: palette.line,
    alignSelf: "stretch",
    justifyContent: "center",
    flexWrap: "wrap",
  },
  location: {
    ...text.caption,
    marginTop: 8,
  },
  accuracyText: {
    ...text.bodyMuted,
  },
  elapsed: {
    ...text.bodyMuted,
    color: palette.brandText,
    textAlign: "center",
    marginTop: 12,
    marginHorizontal: 20,
  },
});
