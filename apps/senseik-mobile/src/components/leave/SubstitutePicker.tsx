import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Modal,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { EmptyState, ErrorBanner, IconCircleButton } from "@/components/ui";
import { fontFor, palette, radius, text } from "@/theme/theme";
import { peopleService, type PersonListItem } from "@/services/PeopleService";

/** Debounce delay (ms) before a search keystroke triggers a directory reload (matches PeopleScreen). */
const SEARCH_DEBOUNCE_MS = 350;

export interface SubstituteCandidate {
  id: string;
  displayName: string;
}

/**
 * B-mobil-005 "Vekil": the requester must never appear as their own substitute option.
 * `excludeEmployeeId` only comes from `authStore`'s own `/identity/me` result - never derived by
 * matching e-mail/name against the directory (a false match would silently hide the wrong person).
 * When the id is unknown (older session, missing claim), nobody is excluded here and the server's
 * own `substitute == self` check (`LeaveRequestErrors.SubstituteIsRequester`) is the backstop.
 */
export function excludeSelf(
  people: PersonListItem[],
  excludeEmployeeId: string | null | undefined
): PersonListItem[] {
  if (!excludeEmployeeId) return people;
  return people.filter((person) => person.id !== excludeEmployeeId);
}

type Props = {
  visible: boolean;
  onClose: () => void;
  onSelect: (candidate: SubstituteCandidate) => void;
  /** The caller's own employeeId, when known - see `excludeSelf`. */
  excludeEmployeeId?: string | null;
};

/** Full-screen searchable modal to pick a "requiresSubstitute" leave type's vekil (D2/B-mobil-005). */
export function SubstitutePicker({ visible, onClose, onSelect, excludeEmployeeId }: Props) {
  const { t } = useTranslation("leave");
  const insets = useSafeAreaInsets();
  const [query, setQuery] = useState("");
  const [people, setPeople] = useState<PersonListItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (q: string) => {
    setLoading(true);
    setError(null);
    try {
      const result = await peopleService.listPeople({ q, pageSize: 20 });
      setPeople(result.items);
    } catch {
      setError(i18n.t("leave:substitute.loadError"));
    } finally {
      setLoading(false);
    }
  }, []);

  // Opening the modal resets the search box and loads the unfiltered first page immediately
  // (the debounced effect below re-loads once the reset `query` change lands, which is harmless).
  useEffect(() => {
    if (!visible) return;
    setQuery("");
    void load("");
  }, [visible, load]);

  useEffect(() => {
    if (!visible) return;
    const handle = setTimeout(() => void load(query), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [query, visible, load]);

  const candidates = excludeSelf(people, excludeEmployeeId);

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={[styles.container, { paddingTop: insets.top + 12 }]}>
        <View style={styles.header}>
          <Text style={styles.title}>{t("substitute.title")}</Text>
          <IconCircleButton
            icon="close"
            onPress={onClose}
            accessibilityLabel={t("common:actions.close")}
          />
        </View>

        <TextInput
          style={styles.searchInput}
          placeholder={t("substitute.searchPlaceholder")}
          placeholderTextColor={palette.textFaint}
          value={query}
          onChangeText={setQuery}
          autoCapitalize="none"
          autoCorrect={false}
          accessibilityLabel={t("substitute.searchA11y")}
        />

        {error ? <ErrorBanner message={error} /> : null}

        {loading ? (
          <ActivityIndicator size="large" color={palette.white} style={styles.loading} />
        ) : (
          <FlatList
            data={candidates}
            keyExtractor={(item) => item.id}
            contentContainerStyle={{ paddingBottom: insets.bottom + 16 }}
            renderItem={({ item }) => (
              <TouchableOpacity
                style={styles.row}
                onPress={() => onSelect({ id: item.id, displayName: item.displayName })}
                accessibilityRole="button"
                accessibilityLabel={item.displayName}
              >
                <Text style={styles.rowName}>{item.displayName}</Text>
                {(item.titleName || item.departmentName) && (
                  <Text style={styles.rowSubtitle}>
                    {[item.titleName, item.departmentName].filter(Boolean).join(" · ")}
                  </Text>
                )}
              </TouchableOpacity>
            )}
            ListEmptyComponent={
              <EmptyState
                icon="people-outline"
                title={t("substitute.empty")}
                message={t("substitute.emptyGuide")}
                actionLabel={t(query.trim() ? "substitute.clearSearch" : "substitute.close")}
                onAction={() => (query.trim() ? setQuery("") : onClose())}
              />
            }
          />
        )}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    paddingHorizontal: 20,
    gap: 14,
    backgroundColor: palette.sheetBottom,
    // New-Architecture-only style prop (RN 0.86); falls back to the solid `backgroundColor` above.
    experimental_backgroundImage: `linear-gradient(180deg, ${palette.sheetTop}, ${palette.sheetBottom})`,
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  title: {
    ...text.title,
  },
  searchInput: {
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontSize: 15,
    color: palette.white,
    ...fontFor(500),
  },
  loading: {
    marginTop: 32,
  },
  row: {
    minHeight: 52,
    justifyContent: "center",
    paddingVertical: 10,
    borderBottomWidth: 1,
    borderBottomColor: palette.line,
  },
  rowName: {
    ...text.body,
    ...fontFor(700),
  },
  rowSubtitle: {
    ...text.caption,
    marginTop: 2,
  },
  emptyText: {
    ...text.bodyMuted,
    textAlign: "center",
    marginTop: 32,
  },
});
