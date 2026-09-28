import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { PUNCH_ACTIONS, punchLabel, type PunchAction } from "@/lib/punch";
import { palette, radius, text } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

const ICONS: Record<PunchAction, IoniconName> = {
  in: "log-in-outline",
  out: "log-out-outline",
  breakStart: "cafe-outline",
  breakEnd: "play-circle-outline",
};

/** Unavailable tiles fade to this opacity (spec §6: "uygun olmayanlar soluk"). */
const UNAVAILABLE_OPACITY = 0.4;

type Props = {
  times: Record<PunchAction, string | null>;
  available: Record<PunchAction, boolean>;
  next: PunchAction | "done";
  /** Disables every tile (a punch in flight, or no location yet) without fading the available ones. */
  disabled: boolean;
  onPress: (action: PunchAction) => void;
};

/** 2×2 Giriş · Çıkış · Mola başı · Mola sonu; the next punch gets the brand border. */
export function PunchTiles({ times, available, next, disabled, onPress }: Props) {
  const { t } = useTranslation("checkin");
  return (
    <View style={styles.grid}>
      {PUNCH_ACTIONS.map((action) => {
        const isNext = next === action;
        const isAvailable = available[action];
        const time = times[action];
        const labels = punchLabel(action);
        const title = labels.tile;
        return (
          <TouchableOpacity
            key={action}
            style={[
              styles.tile,
              isNext && styles.tileNext,
              !isAvailable && { opacity: UNAVAILABLE_OPACITY },
            ]}
            onPress={() => onPress(action)}
            disabled={disabled || !isAvailable}
            accessibilityRole="button"
            accessibilityLabel={t("tiles.a11y", { title, time: time ?? t("tiles.noRecord") })}
            accessibilityHint={isAvailable ? labels.action : undefined}
            accessibilityState={{ disabled: disabled || !isAvailable, selected: isNext }}
            testID={`punch-tile-${action}`}
          >
            <Ionicons
              name={ICONS[action]}
              size={22}
              color={isNext ? palette.brandText : palette.ice}
            />
            <Text style={styles.title}>{title}</Text>
            <Text style={styles.time}>{time ?? "--:--"}</Text>
          </TouchableOpacity>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  grid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    marginHorizontal: 20,
    marginTop: 16,
  },
  tile: {
    flexBasis: "46%",
    flexGrow: 1,
    minHeight: 96,
    padding: 14,
    gap: 4,
    borderRadius: radius.tile,
    backgroundColor: palette.glass,
    borderWidth: 2,
    borderColor: palette.glassBorder,
  },
  tileNext: {
    borderColor: palette.brand,
  },
  title: {
    ...text.bodyMuted,
    marginTop: 4,
  },
  time: {
    ...text.number,
  },
});
