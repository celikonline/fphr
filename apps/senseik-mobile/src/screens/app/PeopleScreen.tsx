import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Linking,
  RefreshControl,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import {
  Avatar,
  EmptyState,
  ErrorBanner,
  GlassCard,
  Screen,
  ScreenHeader,
  TextField,
} from "@/components/ui";
import { peopleService, type PersonListItem } from "@/services/PeopleService";
import type { AppStackParamList } from "@/types/navigation";
import { palette, text } from "@/theme/theme";

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
  const { t } = useTranslation("people");
  const subtitle = [person.titleName, person.departmentName].filter(Boolean).join(" · ");

  return (
    <GlassCard style={styles.card}>
      <View style={styles.header}>
        <Avatar name={person.displayName} employeeId={person.id} size={48} radius={16} />
        <View style={styles.headerText}>
          <Text style={styles.name} numberOfLines={1}>
            {person.displayName}
          </Text>
          {subtitle ? (
            <Text style={styles.subtitle} numberOfLines={1}>
              {subtitle}
            </Text>
          ) : null}
        </View>
      </View>
      <View style={styles.contactRow}>
        {person.workPhone ? (
          <TouchableOpacity
            style={styles.contactButton}
            onPress={() => openPersonPhone(person.workPhone)}
            accessibilityRole="button"
            accessibilityLabel={t("callA11y", { name: person.displayName })}
          >
            <Ionicons name="call-outline" size={16} color={palette.brandText} />
            <Text style={styles.contactButtonText}>{person.workPhone}</Text>
          </TouchableOpacity>
        ) : null}
        {person.workEmail ? (
          <TouchableOpacity
            style={styles.contactButton}
            onPress={() => openPersonEmail(person.workEmail)}
            accessibilityRole="button"
            accessibilityLabel={t("emailA11y", { name: person.displayName })}
          >
            <Ionicons name="mail-outline" size={16} color={palette.brandText} />
            <Text style={styles.contactButtonText} numberOfLines={1}>
              {person.workEmail}
            </Text>
          </TouchableOpacity>
        ) : null}
      </View>
    </GlassCard>
  );
}

export default function PeopleScreen() {
  const { t } = useTranslation("people");
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
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
      setError(i18n.t("people:loadError"));
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

  const header = <ScreenHeader title={t("title")} />;

  return (
    <Screen scroll={false} header={header}>
      <View style={styles.flex}>
        <View style={styles.searchWrap}>
          <TextField
            label={t("search")}
            placeholder={t("searchPlaceholder")}
            value={search}
            onChangeText={setSearch}
            autoCapitalize="none"
            autoCorrect={false}
          />
        </View>

        {error ? <ErrorBanner message={error} onRetry={() => void load(search)} /> : null}

        {loading ? (
          <View style={styles.centered}>
            <ActivityIndicator size="large" color={palette.white} />
          </View>
        ) : (
          <FlatList
            data={people}
            keyExtractor={(item) => item.id}
            renderItem={({ item }) => <PersonRow person={item} />}
            contentContainerStyle={styles.listContent}
            refreshControl={
              <RefreshControl
                refreshing={refreshing}
                onRefresh={() => void onRefresh()}
                tintColor={palette.white}
              />
            }
            ListEmptyComponent={
              <EmptyState
                icon="people-outline"
                title={t("empty")}
                message={t(search.trim() ? "searchEmptyGuide" : "emptyGuide")}
                actionLabel={t(search.trim() ? "clearSearch" : "getHelp")}
                onAction={() =>
                  search.trim() ? setSearch("") : navigation.navigate("Support")
                }
              />
            }
          />
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  searchWrap: {
    paddingHorizontal: 20,
    paddingBottom: 12,
  },
  listContent: {
    paddingHorizontal: 20,
    paddingBottom: 12,
  },
  card: {
    marginBottom: 10,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  headerText: {
    flex: 1,
    minWidth: 0,
  },
  name: {
    ...text.heading,
  },
  subtitle: {
    ...text.bodyMuted,
    marginTop: 2,
  },
  contactRow: {
    marginTop: 12,
    gap: 8,
  },
  contactButton: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  contactButtonText: {
    ...text.bodyMuted,
    color: palette.brandText,
    flexShrink: 1,
  },
});
