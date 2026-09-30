import React, { Fragment } from "react";
import { StyleSheet, Text, View } from "react-native";
import { GlassCard } from "@/components/ui";
import { palette, text } from "@/theme/theme";
import type { ProfileField } from "./profileGroups";

type Props = {
  fields: ProfileField[];
};

/** One profile group's info rows (label caption + value body), inside a single glass card. */
export function ProfileInfoCard({ fields }: Props) {
  return (
    <GlassCard style={styles.card}>
      {fields.map((field, index) => (
        <Fragment key={field.label}>
          {index > 0 ? <View style={styles.separator} /> : null}
          <View style={styles.row}>
            <Text style={styles.label}>{field.label}</Text>
            <Text style={styles.value}>{field.value}</Text>
          </View>
        </Fragment>
      ))}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 16,
  },
  row: {
    gap: 4,
  },
  label: {
    ...text.caption,
  },
  value: {
    ...text.body,
  },
  separator: {
    height: 1,
    marginVertical: 12,
    backgroundColor: palette.line,
  },
});
