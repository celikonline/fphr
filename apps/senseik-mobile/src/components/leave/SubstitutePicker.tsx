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
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
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
      setError("Çalışan listesi yüklenemedi. Lütfen tekrar deneyin.");
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
      <View style={styles.container}>
        <View style={styles.header}>
          <Text style={styles.title}>Vekil seç</Text>
          <TouchableOpacity onPress={onClose}>
            <Text style={styles.closeLink}>Kapat</Text>
          </TouchableOpacity>
        </View>

        <TextInput
          style={styles.searchInput}
          placeholder="İsim, unvan veya birim ara"
          placeholderTextColor={colors.textTertiary}
          value={query}
          onChangeText={setQuery}
          autoCapitalize="none"
          autoCorrect={false}
        />

        {error && (
          <View style={styles.errorBanner}>
            <Text style={styles.errorText}>{error}</Text>
          </View>
        )}

        {loading ? (
          <ActivityIndicator size="large" color={colors.primary} style={styles.loading} />
        ) : (
          <FlatList
            data={candidates}
            keyExtractor={(item) => item.id}
            renderItem={({ item }) => (
              <TouchableOpacity
                style={styles.row}
                onPress={() => onSelect({ id: item.id, displayName: item.displayName })}
              >
                <Text style={styles.rowName}>{item.displayName}</Text>
                {(item.titleName || item.departmentName) && (
                  <Text style={styles.rowSubtitle}>
                    {[item.titleName, item.departmentName].filter(Boolean).join(" · ")}
                  </Text>
                )}
              </TouchableOpacity>
            )}
            ListEmptyComponent={<Text style={styles.emptyText}>Kayıt bulunamadı.</Text>}
          />
        )}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
    padding: spacing.xl,
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: spacing.lg,
  },
  title: {
    ...typography.h3,
    color: colors.text,
  },
  closeLink: {
    ...typography.body,
    color: colors.primary,
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
  loading: {
    marginTop: spacing.xxl,
  },
  row: {
    paddingVertical: spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
  },
  rowName: {
    ...typography.body,
    color: colors.text,
  },
  rowSubtitle: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: 2,
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
