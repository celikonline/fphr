import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, Text, View } from "react-native";
import { ErrorBanner, PrimaryButton, SelectField, TextField } from "@/components/ui";
import { OptionSheet } from "@/components/form/OptionSheet";
import { SheetDateField } from "@/components/form/SheetDateField";
import {
  expenseService,
  ExpenseClaimDraftIncompleteError,
  EXPENSE_DEFAULT_CURRENCY,
  type ExpenseCategory,
} from "@/services/ExpenseService";
import { toLocalDateOnly } from "@/lib/formatters";
import { isPositiveAmount, isReceiptDateAllowed, parseAmountInput } from "@/lib/requestValidation";
import { expenseCategoryLabel } from "@/lib/expenseLabels";
import { getApiErrorMessage } from "@/lib/apiError";
import { palette } from "@/theme/theme";
import { requestFormStyles as styles } from "./requestFormStyles";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/**
 * TK-031 decision #2/#3: v1 is single-item (draft -> add the one item -> submit) and never sends
 * `receiptFileId` (no photo upload yet - `ReceiptFileId` is nullable on `AddExpenseItemRequest`, so
 * omitting it is a valid, supported request, not a missing-required-field gap).
 */
export function canSubmitExpenseClaim(params: {
  categoryId: string | null;
  amountText: string;
  receiptDate: string;
}): boolean {
  if (!params.categoryId) return false;
  const amount = parseAmountInput(params.amountText);
  if (amount === null || !isPositiveAmount(amount)) return false;
  return isReceiptDateAllowed(params.receiptDate);
}

type Props = {
  /** Called after a successful submit (NewRequest closes itself with it). */
  onSubmitted?: () => void;
};

/**
 * "Masraf talebi" form (NewRequest body). The caller's own claim history lives on Taleplerim
 * (`RequestsScreen`).
 */
export default function ExpenseClaimSection({ onSubmitted }: Props) {
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [categorySheetVisible, setCategorySheetVisible] = useState(false);

  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [amountText, setAmountText] = useState("");
  const [receiptDate, setReceiptDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");

  const loadCategories = useCallback(async () => {
    setError(null);
    try {
      const active = (await expenseService.getCategories()).filter((c) => c.isActive);
      setCategories(active);
      setCategoryId((current) => current ?? active[0]?.id ?? null);
    } catch {
      setError("Harcama bilgileri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadCategories().finally(() => setLoading(false));
  }, [loadCategories]);

  async function submit() {
    if (!categoryId) return;
    const amount = parseAmountInput(amountText);
    if (amount === null || !isPositiveAmount(amount)) {
      Alert.alert("Hata", "Lütfen geçerli, sıfırdan büyük bir tutar girin.");
      return;
    }
    if (!isReceiptDateAllowed(receiptDate)) {
      Alert.alert("Hata", "Fiş tarihi gelecekte olamaz.");
      return;
    }

    setSubmitting(true);
    try {
      await expenseService.submitSingleItemExpenseClaim({
        categoryId,
        amount,
        receiptDate,
        currency: EXPENSE_DEFAULT_CURRENCY,
        description: description.trim().length > 0 ? description.trim() : null,
      });
      setAmountText("");
      setDescription("");
      setReceiptDate(todayDateOnly());
      onSubmitted?.();
      Alert.alert("Gönderildi", "Masraf talebiniz onaya gönderildi.");
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
        <ActivityIndicator size="large" color={palette.white} />
      </View>
    );
  }

  const selectedCategory = categories.find((c) => c.id === categoryId);
  const canSubmit = canSubmitExpenseClaim({ categoryId, amountText, receiptDate }) && !submitting;

  return (
    <View style={styles.form}>
      {error ? <ErrorBanner message={error} onRetry={() => void loadCategories()} /> : null}

      {categories.length === 0 ? (
        <Text style={styles.hint}>Aktif harcama kategorisi bulunamadı.</Text>
      ) : (
        <SelectField
          label="Kategori"
          required
          value={selectedCategory ? expenseCategoryLabel(selectedCategory.code) : undefined}
          placeholder="Kategori seçin"
          onPress={() => setCategorySheetVisible(true)}
        />
      )}
      <TextField
        label="Tutar (TRY)"
        required
        value={amountText}
        onChangeText={setAmountText}
        placeholder="0,00"
        keyboardType="decimal-pad"
      />
      <SheetDateField label="Fiş tarihi" required value={receiptDate} onChange={setReceiptDate} />
      <TextField
        label="Açıklama (opsiyonel)"
        value={description}
        onChangeText={setDescription}
        placeholder="Örn. müşteri ziyareti"
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />
      <View style={styles.submit}>
        <PrimaryButton
          label="Talebi gönder"
          onPress={() => void submit()}
          loading={submitting}
          disabled={!canSubmit}
        />
      </View>

      <OptionSheet
        visible={categorySheetVisible}
        title="Kategori"
        options={categories.map((c) => ({ value: c.id, label: expenseCategoryLabel(c.code) }))}
        selected={categoryId}
        onSelect={setCategoryId}
        onClose={() => setCategorySheetVisible(false)}
      />
    </View>
  );
}
