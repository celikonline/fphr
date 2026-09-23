import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  leaveService,
  type EmployeeLeaveBalances,
  type LeaveRequest,
  type RequestableLeaveType,
} from "@/services/LeaveService";
import { LeaveRequestListItem } from "@/components/leave/LeaveRequestListItem";

/** `DateOnly` string the API expects ("yyyy-MM-dd"); today, as a sane default for the form. */
function todayDateOnly(): string {
  return new Date().toISOString().slice(0, 10);
}

export default function LeaveScreen() {
  const [balances, setBalances] = useState<EmployeeLeaveBalances | null>(null);
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [requestableTypes, setRequestableTypes] = useState<RequestableLeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [selectedTypeId, setSelectedTypeId] = useState<string | null>(null);
  const [startDate, setStartDate] = useState(todayDateOnly());
  const [endDate, setEndDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const [balancesResult, requestsResult, typesResult] = await Promise.all([
        leaveService.getMyBalances(),
        leaveService.getMyRequests(),
        leaveService.getMyRequestableLeaveTypes(),
      ]);
      setBalances(balancesResult);
      setRequests(requestsResult);
      setRequestableTypes(typesResult);
      setSelectedTypeId((current) => current ?? typesResult[0]?.id ?? null);
    } catch {
      setError("İzin bilgileri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  const selectedType = requestableTypes.find((t) => t.id === selectedTypeId) ?? null;

  async function submitRequest() {
    if (!selectedTypeId) {
      Alert.alert("Eksik bilgi", "Lütfen bir izin türü seçin.");
      return;
    }
    if (!startDate || !endDate) {
      Alert.alert("Eksik bilgi", "Lütfen başlangıç ve bitiş tarihini girin.");
      return;
    }
    if (endDate < startDate) {
      Alert.alert("Geçersiz tarih", "Bitiş tarihi başlangıç tarihinden önce olamaz.");
      return;
    }
    if (selectedType?.requiresDescription && description.trim().length === 0) {
      Alert.alert("Eksik bilgi", "Bu izin türü için açıklama zorunludur.");
      return;
    }

    setSubmitting(true);
    try {
      await leaveService.submitLeaveRequest({
        leaveTypeId: selectedTypeId,
        startDate,
        endDate,
        description: description.trim().length > 0 ? description.trim() : null,
      });
      setDescription("");
      await loadAll();
      Alert.alert("Gönderildi", "İzin talebiniz onaya gönderildi.");
    } catch {
      Alert.alert("Hata", "İzin talebi gönderilemedi. Lütfen tekrar deneyin.");
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>İzinlerim</Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <Text style={styles.sectionTitle}>Bakiyelerim</Text>
      {balances && balances.balances.length > 0 ? (
        balances.balances.map((item) => (
          <View key={item.leaveTypeCode + item.leaveTypeName} style={styles.card}>
            <Text style={styles.cardLabel}>{item.leaveTypeName}</Text>
            <Text style={styles.cardValue}>{item.balance.availableBalance} gün kullanılabilir</Text>
            <Text style={styles.cardSubtext}>
              {item.balance.totalAccrued} tahakkuk · {item.balance.totalUsed} kullanılan
            </Text>
          </View>
        ))
      ) : (
        <Text style={styles.emptyText}>Bakiye kaydı bulunamadı.</Text>
      )}

      <Text style={styles.sectionTitle}>Yeni izin talebi</Text>
      <View style={styles.card}>
        {requestableTypes.length === 0 ? (
          <Text style={styles.emptyText}>Talep edebileceğiniz bir izin türü yok.</Text>
        ) : (
          <>
            <Text style={styles.fieldLabel}>İzin türü</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipRow}>
              {requestableTypes.map((type) => {
                const selected = type.id === selectedTypeId;
                return (
                  <TouchableOpacity
                    key={type.id}
                    style={[styles.chip, selected && styles.chipSelected]}
                    onPress={() => setSelectedTypeId(type.id)}
                  >
                    <Text style={[styles.chipText, selected && styles.chipTextSelected]}>
                      {type.name}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </ScrollView>

            <Text style={styles.fieldLabel}>Başlangıç tarihi (yyyy-aa-gg)</Text>
            <TextInput
              style={styles.input}
              value={startDate}
              onChangeText={setStartDate}
              placeholder="2026-09-16"
              autoCapitalize="none"
            />

            <Text style={styles.fieldLabel}>Bitiş tarihi (yyyy-aa-gg)</Text>
            <TextInput
              style={styles.input}
              value={endDate}
              onChangeText={setEndDate}
              placeholder="2026-09-18"
              autoCapitalize="none"
            />

            <Text style={styles.fieldLabel}>
              Açıklama {selectedType?.requiresDescription ? "(zorunlu)" : "(opsiyonel)"}
            </Text>
            <TextInput
              style={[styles.input, styles.textArea]}
              value={description}
              onChangeText={setDescription}
              placeholder="Talebinizle ilgili not ekleyin"
              multiline
              numberOfLines={3}
            />

            <TouchableOpacity
              style={[styles.submitButton, submitting && styles.submitButtonDisabled]}
              onPress={() => void submitRequest()}
              disabled={submitting}
            >
              {submitting ? (
                <ActivityIndicator color={colors.white} />
              ) : (
                <Text style={styles.submitButtonText}>Talebi gönder</Text>
              )}
            </TouchableOpacity>
          </>
        )}
      </View>

      <Text style={styles.sectionTitle}>Taleplerim</Text>
      {requests.length === 0 ? (
        <Text style={styles.emptyText}>Henüz izin talebiniz yok.</Text>
      ) : (
        requests.map((request) => <LeaveRequestListItem key={request.id} request={request} />)
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardLabel: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  cardValue: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xs,
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
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
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  chipRow: {
    flexDirection: "row",
  },
  chip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    marginRight: spacing.sm,
    backgroundColor: colors.gray50,
  },
  chipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  chipText: {
    ...typography.bodySm,
    color: colors.text,
  },
  chipTextSelected: {
    color: colors.white,
  },
  input: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    backgroundColor: colors.white,
  },
  textArea: {
    minHeight: 72,
    textAlignVertical: "top",
  },
  submitButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.md,
    alignItems: "center",
    marginTop: spacing.lg,
  },
  submitButtonDisabled: {
    opacity: 0.6,
  },
  submitButtonText: {
    ...typography.button,
    color: colors.white,
  },
});
