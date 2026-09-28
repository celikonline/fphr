import React from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { GlassCard, PrimaryButton, TextField } from "@/components/ui";
import { SUPPORT_TICKET_CATEGORIES, type SupportTicketCategory } from "@/services/SupportService";
import { fontFor, palette, radius, text } from "@/theme/theme";

type Props = {
  category: SupportTicketCategory;
  onCategoryChange: (value: SupportTicketCategory) => void;
  subject: string;
  onSubjectChange: (value: string) => void;
  body: string;
  onBodyChange: (value: string) => void;
  submitting: boolean;
  onSubmit: () => void;
};

/** "Desteğe bağlan": category chips + subject/body fields + submit. */
export function NewTicketCard({
  category,
  onCategoryChange,
  subject,
  onSubjectChange,
  body,
  onBodyChange,
  submitting,
  onSubmit,
}: Props) {
  return (
    <GlassCard style={styles.card}>
      <Text style={styles.intro}>
        Aradığınızı bulamadınız mı? Kısa bir talep açın, ekibimiz size dönsün.
      </Text>

      <Text style={styles.fieldLabel}>Kategori</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipRow}>
        {SUPPORT_TICKET_CATEGORIES.map((option) => {
          const selected = option.value === category;
          return (
            <TouchableOpacity
              key={option.value}
              style={[styles.chip, selected && styles.chipSelected]}
              onPress={() => onCategoryChange(option.value)}
              accessibilityRole="button"
              accessibilityState={{ selected }}
              accessibilityLabel={option.label}
            >
              <Text style={[styles.chipText, selected && styles.chipTextSelected]}>
                {option.label}
              </Text>
            </TouchableOpacity>
          );
        })}
      </ScrollView>

      <View style={styles.field}>
        <TextField
          label="Konu"
          value={subject}
          onChangeText={onSubjectChange}
          placeholder="Talebinizi kısaca özetleyin"
        />
      </View>

      <View style={styles.field}>
        <TextField
          label="Ayrıntı"
          value={body}
          onChangeText={onBodyChange}
          placeholder="Ne yapmaya çalışıyordunuz, ne bekliyordunuz, ne oldu?"
          multiline
          numberOfLines={3}
          style={styles.textArea}
        />
      </View>

      <View style={styles.submit}>
        <PrimaryButton label="Talep gönder" onPress={onSubmit} loading={submitting} />
      </View>
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 16,
  },
  intro: {
    ...text.bodyMuted,
  },
  fieldLabel: {
    ...text.caption,
    marginTop: 16,
    marginBottom: 8,
  },
  chipRow: {
    flexDirection: "row",
  },
  chip: {
    paddingVertical: 8,
    paddingHorizontal: 14,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    backgroundColor: palette.glass,
    marginRight: 8,
  },
  chipSelected: {
    backgroundColor: palette.brand,
    borderColor: palette.brand,
  },
  chipText: {
    fontSize: 13,
    color: palette.ice,
    ...fontFor(700),
  },
  chipTextSelected: {
    color: palette.white,
  },
  field: {
    marginTop: 14,
  },
  textArea: {
    minHeight: 72,
    textAlignVertical: "top",
  },
  submit: {
    marginTop: 16,
  },
});
