import React, { Fragment, type ReactNode } from "react";
import { StyleSheet, Text, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { Avatar, GlassCard } from "@/components/ui";
import { formatDays } from "@/lib/formatters";
import type { PersonOff } from "@/services/HomeFeedService";
import { fontFor, palette, text } from "@/theme/theme";
import { formatShortDate } from "./homeSections";

type PersonRowProps = {
  name: string;
  employeeId: string;
  emoji: string;
  pillTitle: string;
  /** Birthday day number ("02"): larger, tabular digits. */
  largePillTitle?: boolean;
  pillCaption: string;
  meta: ReactNode;
};

/**
 * Mockup `.prow` + `.pname` + `.pmeta`: avatar, a small emoji badge overlapping it, and a side
 * info pill tucked under both; the person's name and a meta line below. Shared with `BirthdayCard`.
 */
export function PersonRow({
  name,
  employeeId,
  emoji,
  pillTitle,
  largePillTitle = false,
  pillCaption,
  meta,
}: PersonRowProps) {
  return (
    <View>
      <View style={styles.row}>
        <View style={styles.avatar}>
          <Avatar name={name} employeeId={employeeId} size={58} radius={16} />
        </View>
        <View
          style={styles.emoji}
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
        >
          <Text style={styles.emojiText}>{emoji}</Text>
        </View>
        <View style={styles.pill}>
          <Text
            style={[styles.pillTitle, largePillTitle && styles.pillTitleLarge]}
            numberOfLines={1}
          >
            {pillTitle}
          </Text>
          <Text style={styles.pillCaption}>{pillCaption}</Text>
        </View>
      </View>
      <Text style={styles.name} numberOfLines={1}>
        {name}
      </Text>
      <View style={styles.meta}>{meta}</View>
    </View>
  );
}

/** Thin divider between rows inside one card (mockup `.sep`). */
export function RowSeparator() {
  return <View style={styles.separator} />;
}

type Props = {
  people: PersonOff[];
};

/** "Bugün izinde olanlar": everyone on approved leave today in one card. */
export function PeopleOffCard({ people }: Props) {
  const { t } = useTranslation("home");
  return (
    <GlassCard style={styles.card}>
      {people.map((person, index) => (
        <Fragment key={person.employeeId}>
          {index > 0 ? <RowSeparator /> : null}
          <PersonRow
            name={person.name}
            employeeId={person.employeeId}
            emoji={person.isRemote ? "💻" : "🌴"}
            pillTitle={person.typeLabel}
            pillCaption={
              person.isRemote
                ? t("peopleOff.today")
                : t("common:units.days", { count: person.days, value: formatDays(person.days) })
            }
            meta={
              <>
                <Text style={styles.metaText}>{formatShortDate(person.startDate)}</Text>
                <Ionicons name="arrow-forward" size={14} color={palette.textMuted} />
                <Text style={styles.metaText}>{formatShortDate(person.endDate)}</Text>
              </>
            }
          />
        </Fragment>
      ))}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 12,
  },
  metaText: {
    ...text.caption,
    fontSize: 13,
    fontVariant: ["tabular-nums"],
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
  },
  avatar: {
    zIndex: 1,
  },
  emoji: {
    zIndex: 2,
    width: 30,
    height: 30,
    marginLeft: -12,
    borderRadius: 15,
    backgroundColor: palette.sheetTop,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    alignItems: "center",
    justifyContent: "center",
  },
  emojiText: {
    fontSize: 15,
  },
  pill: {
    flex: 1,
    minWidth: 0,
    marginLeft: -15,
    paddingVertical: 10,
    paddingLeft: 24,
    paddingRight: 12,
    borderRadius: 16,
    backgroundColor: palette.glassInner,
    borderWidth: 1,
    borderColor: palette.line,
    alignItems: "center",
  },
  pillTitle: {
    fontSize: 14,
    lineHeight: 20,
    color: palette.white,
    ...fontFor(800),
  },
  pillTitleLarge: {
    fontSize: 20,
    lineHeight: 26,
    fontVariant: ["tabular-nums"],
  },
  pillCaption: {
    ...text.caption,
    fontSize: 12.5,
  },
  name: {
    marginTop: 12,
    fontSize: 15,
    lineHeight: 20,
    color: palette.white,
    ...fontFor(800),
  },
  meta: {
    marginTop: 3,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  separator: {
    height: 1,
    marginVertical: 14,
    backgroundColor: palette.line,
  },
});
