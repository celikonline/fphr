import React, { Fragment } from "react";
import { StyleSheet, Text } from "react-native";
import { GlassCard } from "@/components/ui";
import type { UpcomingBirthday } from "@/services/HomeFeedService";
import { text } from "@/theme/theme";
import { birthdayCountdown, dayAndMonth } from "./homeSections";
import { PersonRow, RowSeparator } from "./PeopleOffCard";

type Props = {
  birthdays: Array<UpcomingBirthday & { daysLeft: number }>;
};

/** "Yaklaşan doğum günleri": day/month pill beside each avatar, "N gün kaldı" below. */
export function BirthdayCard({ birthdays }: Props) {
  return (
    <GlassCard style={styles.card}>
      {birthdays.map((birthday, index) => {
        const { day, month } = dayAndMonth(birthday.nextBirthday);
        return (
          <Fragment key={birthday.id}>
            {index > 0 ? <RowSeparator /> : null}
            <PersonRow
              name={birthday.displayName}
              employeeId={birthday.id}
              emoji="🎂"
              pillTitle={day}
              largePillTitle
              pillCaption={month}
              meta={<Text style={styles.metaText}>{birthdayCountdown(birthday.daysLeft)}</Text>}
            />
          </Fragment>
        );
      })}
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
});
