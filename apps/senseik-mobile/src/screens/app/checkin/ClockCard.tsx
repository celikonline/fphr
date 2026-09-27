import React, { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { GlassCard, StatusPill } from "@/components/ui";
import { accuracyLabel, elapsedSinceCheckIn, formatClockDate, formatGmtOffset } from "@/lib/punch";
import { palette, text } from "@/theme/theme";

const TICK_MS = 1000;

type Props = {
  /** Ticking pauses while false (tab not focused), so a background tab never re-renders every second. */
  active: boolean;
  /** Today's check-in ("HH:mm:ss") while still working; drives the "Girişten beri …" line. */
  workingSince: string | null;
  /** `expo-location` `coords.accuracy` in meters; `null` when unknown. */
  accuracyMeters: number | null;
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
export function ClockCard({ active, workingSince, accuracyMeters }: Props) {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    if (!active) return undefined;
    setNow(new Date());
    const timer = setInterval(() => setNow(new Date()), TICK_MS);
    return () => clearInterval(timer);
  }, [active]);

  const accuracy = accuracyLabel(accuracyMeters);
  const elapsed = workingSince ? elapsedSinceCheckIn(workingSince, now) : null;
  const clock = formatClock(now);

  return (
    <>
      <GlassCard style={styles.card}>
        <Text
          style={styles.clock}
          accessibilityRole="text"
          accessibilityLabel={`Saat ${clock.slice(0, 5)}`}
        >
          {clock}
        </Text>
        <Text style={styles.caption}>
          {formatGmtOffset(now)} · {formatClockDate(now)}
        </Text>
        <View style={styles.accuracyRow}>
          <Text style={styles.accuracyText}>
            Konum doğruluğu:{" "}
            {accuracyMeters === null ? "bilinmiyor" : `${Math.round(accuracyMeters)} m`}
          </Text>
          {accuracy ? (
            <StatusPill label={accuracy} tone={accuracy === "Doğru" ? "success" : "warning"} />
          ) : null}
        </View>
      </GlassCard>
      {elapsed ? <Text style={styles.elapsed}>Girişten beri {elapsed}</Text> : null}
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
