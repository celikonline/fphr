import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Linking,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { peopleService, type PersonListItem } from "@/services/PeopleService";

/** Debounce delay (ms) before a search keystroke triggers a directory reload. */
const SEARCH_DEBOUNCE_MS = 350;

export function openPersonPhone(workPhone: string | null | undefined): void {
  if (!workPhone) return;
  void Linking.openURL(`tel:${workPhone}`);
}

export function openPersonEmail(workEmail: string | null | undefined): void {
  if (!workEmail) return;
  void Linking.openURL(`mailto:${workEmail}`);
}

function PersonRow({ person }: { person: PersonListItem }) {
  const subtitle = [person.titleName, person.departmentName].filter(Boolean).join(" · ");

  return (
    <View style={styles.card}>
      <Text style={styles.name}>{person.displayName}</Text>
      {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
      <View style={styles.contactRow}>
        {person.workPhone ? (
          <TouchableOpacity
            style={styles.contactButton}
            onPress={() => openPersonPhone(person.workPhone)}
            accessibilityRole="button"
            accessibilityLabel={`${person.displayName} - telefon ile ara`}
          >
            <Text style={styles.contactButtonText}>{person.workPhone}</Text>
          </TouchableOpacity>
        ) : null}
        {person.workEmail ? (
          <TouchableOpacity
            style={styles.contactButton}
            onPress={() => openPersonEmail(person.workEmail)}
            accessibilityRole="button"
            accessibilityLabel={`${person.displayName} - e-posta gönder`}
          >
            <Text style={styles.contactButtonText}>{person.workEmail}</Text>
          </TouchableOpacity>
        ) : null}
      </View>
    </View>
  );
}

export default function PeopleScreen() {
  const [people, setPeople] = useState<PersonListItem[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (q: string) => {
    setError(null);
    try {
      const result = await peopleService.listPeople({ q, pageSize: 100 });
      setPeople(result.items);
    } catch {
      setError("Çalışan listesi yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void load(search).finally(() => setLoading(false));
    // Only the initial mount loads with a spinner; later searches debounce below.
  }, []);

  useEffect(() => {
    const handle = setTimeout(() => {
      void load(search);
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [search, load]);

  async function onRefresh() {
    setRefreshing(true);
    await load(search);
    setRefreshing(false);
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Kişiler</Text>

      <TextInput
        style={styles.searchInput}
        placeholder="İsim, unvan veya birim ara"
        placeholderTextColor={colors.textTertiary}
        value={search}
        onChangeText={setSearch}
        autoCapitalize="none"
        autoCorrect={false}
      />

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      {loading ? (
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={colors.primary} />
        </View>
      ) : (
        <FlatList
          data={people}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <PersonRow person={item} />}
          contentContainerStyle={styles.listContent}
          refreshControl={
            <RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />
          }
          ListEmptyComponent={<Text style={styles.emptyText}>Kayıt bulunamadı.</Text>}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
    padding: spacing.xl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  searchInput: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    marginBottom: spacing.md,
  },
  listContent: {
    paddingBottom: spacing.xxxl,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.md,
  },
  name: {
    ...typography.h4,
    color: colors.text,
  },
  subtitle: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: 2,
  },
  contactRow: {
    marginTop: spacing.sm,
    gap: spacing.xs,
  },
  contactButton: {
    paddingVertical: spacing.xs,
  },
  contactButtonText: {
    ...typography.bodySm,
    color: colors.primary,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
    textAlign: "center",
    marginTop: spacing.xxl,
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
});
