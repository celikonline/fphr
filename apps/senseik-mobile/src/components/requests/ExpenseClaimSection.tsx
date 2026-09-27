import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  Alert,
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
  expenseService,
  ExpenseClaimDraftIncompleteError,
  EXPENSE_DEFAULT_CURRENCY,
  type AddExpenseItemInput,
  type ExpenseCategory,
  type ExpenseClaim,
} from "@/services/ExpenseService";
import { DateField } from "@/components/form/DateField";
import { formatCurrencyTRY, toLocalDateOnly } from "@/lib/formatters";
import { formatLeaveDateOnly } from "@/components/leave/LeaveRequestListItem";
import {
  EXPENSE_CLAIM_MAX_ITEMS,
  isPositiveAmount,
  isReceiptDateAllowed,
  parseAmountInput,
} from "@/lib/requestValidation";
import { EXPENSE_CLAIM_STATUS_LABELS, expenseCategoryLabel } from "@/lib/expenseLabels";
import { getApiErrorMessage } from "@/lib/apiError";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/** One not-yet-submitted item the employee has added to the claim being drafted. */
export interface DraftExpenseItem extends AddExpenseItemInput {
  /** Client-only key for the list/removal - never sent to the API. */
  localId: string;
}

/**
 * Web parity (TK-031 follow-up): a claim can hold several items, each validated the same way a
 * single one used to be. Never sends `receiptFileId` (no photo upload yet on mobile - web doesn't
 * upload one either - `ReceiptFileId` is nullable on `AddExpenseItemRequest`, so omitting it is a
 * valid, supported request, not a missing-required-field gap).
 */
export function canAddExpenseItem(params: {
  categoryId: string | null;
  amountText: string;
  receiptDate: string;
  itemCount: number;
}): boolean {
  if (params.itemCount >= EXPENSE_CLAIM_MAX_ITEMS) return false;
  if (!params.categoryId) return false;
  const amount = parseAmountInput(params.amountText);
  if (amount === null || !isPositiveAmount(amount)) return false;
  return isReceiptDateAllowed(params.receiptDate);
}

/** A claim needs at least one added item before it can be submitted. */
export function canSubmitExpenseClaim(params: { itemCount: number }): boolean {
  return params.itemCount > 0;
}

export function sumExpenseItemAmounts(items: { amount: number }[]): number {
  return items.reduce((total, item) => total + item.amount, 0);
}

export default function ExpenseClaimSection() {
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [claims, setClaims] = useState<ExpenseClaim[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [amountText, setAmountText] = useState("");
  const [receiptDate, setReceiptDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");
  const [items, setItems] = useState<DraftExpenseItem[]>([]);
  const nextLocalId = useRef(0);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const [categoriesResult, claimsResult] = await Promise.all([
        expenseService.getCategories(),
        expenseService.getMyExpenseClaims(),
      ]);
      const active = categoriesResult.filter((c) => c.isActive);
      setCategories(active);
      setCategoryId((current) => current ?? active[0]?.id ?? null);
      setClaims(claimsResult);
    } catch {
      setError("Harcama bilgileri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  function addItem() {
    const amount = parseAmountInput(amountText);
    if (!categoryId) {
      Alert.alert("Hata", "Lütfen bir kategori seçin.");
      return;
    }
    if (amount === null || !isPositiveAmount(amount)) {
      Alert.alert("Hata", "Lütfen geçerli, sıfırdan büyük bir tutar girin.");
      return;
    }
    if (!isReceiptDateAllowed(receiptDate)) {
      Alert.alert("Hata", "Fiş tarihi gelecekte olamaz.");
      return;
    }
    if (items.length >= EXPENSE_CLAIM_MAX_ITEMS) {
      Alert.alert("Hata", `Bir talepte en fazla ${EXPENSE_CLAIM_MAX_ITEMS} kalem olabilir.`);
      return;
    }

    const newItem: DraftExpenseItem = {
      localId: String(nextLocalId.current++),
      categoryId,
      amount,
      receiptDate,
      currency: EXPENSE_DEFAULT_CURRENCY,
      description: description.trim().length > 0 ? description.trim() : null,
    };
    setItems((current) => [...current, newItem]);
    setAmountText("");
    setDescription("");
    setReceiptDate(todayDateOnly());
  }

  function removeItem(localId: string) {
    setItems((current) => current.filter((item) => item.localId !== localId));
  }

  async function submit() {
    if (items.length === 0) return;

    setSubmitting(true);
    try {
      const inputs: AddExpenseItemInput[] = items.map((item) => ({
        categoryId: item.categoryId,
        amount: item.amount,
        receiptDate: item.receiptDate,
        currency: item.currency,
        description: item.description,
      }));
      await expenseService.submitExpenseClaim(inputs);
      setItems([]);
      await loadAll();
    } catch (submitError) {
      if (submitError instanceof ExpenseClaimDraftIncompleteError) {
        Alert.alert(
          "Taslak oluşturuldu",
          "Harcama taslağınız oluşturuldu ancak tamamlanamadı. Lütfen web uygulamasından devam edin."
        );
      } else {
        Alert.alert(
          "Hata",
          getApiErrorMessage(submitError, "Harcama talebi gönderilemedi. Lütfen tekrar deneyin.")
        );
      }
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

  const canAddItem =
    canAddExpenseItem({ categoryId, amountText, receiptDate, itemCount: items.length }) &&
    !submitting;
  const canSubmit = canSubmitExpenseClaim({ itemCount: items.length }) && !submitting;
  const total = sumExpenseItemAmounts(items);

  return (
    <View>
      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <Text style={styles.fieldLabel}>Kategori</Text>
      {categories.length === 0 ? (
        <Text style={styles.emptyText}>Aktif harcama kategorisi bulunamadı.</Text>
      ) : (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipRow}>
          {categories.map((category) => {
            const selected = category.id === categoryId;
            return (
              <TouchableOpacity
                key={category.id}
                style={[styles.chip, selected && styles.chipSelected]}
                onPress={() => setCategoryId(category.id)}
              >
                <Text style={[styles.chipText, selected && styles.chipTextSelected]}>
                  {expenseCategoryLabel(category.code)}
                </Text>
              </TouchableOpacity>
            );
          })}
        </ScrollView>
      )}

      <Text style={styles.fieldLabel}>Tutar (TRY)</Text>
      <TextInput
        style={styles.input}
        value={amountText}
        onChangeText={setAmountText}
        placeholder="0,00"
        keyboardType="decimal-pad"
      />

      <Text style={styles.fieldLabel}>Fiş tarihi</Text>
      <DateField value={receiptDate} onChange={setReceiptDate} />

      <Text style={styles.fieldLabel}>Açıklama (opsiyonel)</Text>
      <TextInput
        style={[styles.input, styles.textArea]}
        value={description}
        onChangeText={setDescription}
        placeholder="Örn. müşteri ziyareti"
        multiline
        numberOfLines={3}
      />

      <TouchableOpacity
        style={[styles.addItemButton, !canAddItem && styles.submitButtonDisabled]}
        onPress={addItem}
        disabled={!canAddItem}
      >
        <Text style={styles.addItemButtonText}>Kalem ekle</Text>
      </TouchableOpacity>

      {items.length > 0 && (
        <>
          <Text style={styles.fieldLabel}>Eklenen kalemler</Text>
          {items.map((item) => (
            <View key={item.localId} style={styles.itemRow}>
              <View style={styles.itemRowText}>
                <Text style={styles.itemRowCategory}>
                  {expenseCategoryLabel(
                    categories.find((c) => c.id === item.categoryId)?.code ?? item.categoryId
                  )}
                </Text>
                <Text style={styles.cardSubtext}>
                  {formatLeaveDateOnly(item.receiptDate)} · {formatCurrencyTRY(item.amount)}
                </Text>
              </View>
              <TouchableOpacity onPress={() => removeItem(item.localId)}>
                <Text style={styles.removeItemText}>Kaldır</Text>
              </TouchableOpacity>
            </View>
          ))}
          <View style={styles.totalRow}>
            <Text style={styles.totalLabel}>Toplam</Text>
            <Text style={styles.totalAmount}>{formatCurrencyTRY(total)}</Text>
          </View>
        </>
      )}

      <TouchableOpacity
        style={[styles.submitButton, !canSubmit && styles.submitButtonDisabled]}
        onPress={() => void submit()}
        disabled={!canSubmit}
      >
        {submitting ? (
          <ActivityIndicator color={colors.white} />
        ) : (
          <Text style={styles.submitButtonText}>Gönder</Text>
        )}
      </TouchableOpacity>

      <Text style={styles.sectionTitle}>Geçmiş harcama taleplerim</Text>
      {claims.length === 0 ? (
        <Text style={styles.emptyText}>Henüz harcama talebiniz yok.</Text>
      ) : (
        claims.map((claim) => (
          <View key={claim.id} style={styles.card}>
            <View style={styles.cardHeader}>
              <Text style={styles.cardAmount}>{formatCurrencyTRY(claim.totalAmount)}</Text>
              <View style={styles.statusBadge}>
                <Text style={styles.statusBadgeText}>
                  {EXPENSE_CLAIM_STATUS_LABELS[claim.status]}
                </Text>
              </View>
            </View>
            {claim.description && <Text style={styles.cardSubtext}>{claim.description}</Text>}
            {claim.items.map((item) => (
              <Text key={item.id} style={styles.cardSubtext}>
                {expenseCategoryLabel(
                  categories.find((c) => c.id === item.categoryId)?.code ?? item.categoryId
                )}{" "}
                · {formatLeaveDateOnly(item.receiptDate)} · {formatCurrencyTRY(item.amount)}
              </Text>
            ))}
          </View>
        ))
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  centered: {
    paddingVertical: spacing.xxxl,
    alignItems: "center",
    justifyContent: "center",
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
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
    marginBottom: spacing.sm,
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
  addItemButton: {
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    alignItems: "center",
    marginTop: spacing.md,
  },
  addItemButtonText: {
    ...typography.button,
    color: colors.primary,
  },
  itemRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    padding: spacing.md,
    marginTop: spacing.sm,
    backgroundColor: colors.white,
  },
  itemRowText: {
    flex: 1,
  },
  itemRowCategory: {
    ...typography.bodySm,
    color: colors.text,
    fontWeight: "600",
  },
  removeItemText: {
    ...typography.bodyXs,
    color: colors.error,
    fontWeight: "600",
    marginLeft: spacing.md,
  },
  totalRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginTop: spacing.md,
    paddingTop: spacing.sm,
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  totalLabel: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  totalAmount: {
    ...typography.h4,
    color: colors.text,
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
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  cardAmount: {
    ...typography.h4,
    color: colors.text,
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  statusBadge: {
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    backgroundColor: colors.gray100,
  },
  statusBadgeText: {
    ...typography.caption,
    color: colors.textSecondary,
  },
});
