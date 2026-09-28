import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, Alert, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { ErrorBanner, PrimaryButton, SelectField, TextField } from "@/components/ui";
import { OptionSheet } from "@/components/form/OptionSheet";
import { SheetDateField } from "@/components/form/SheetDateField";
import {
  expenseService,
  ExpenseClaimDraftIncompleteError,
  EXPENSE_DEFAULT_CURRENCY,
  type AddExpenseItemInput,
  type ExpenseCategory,
} from "@/services/ExpenseService";
import { toLocalDateOnly } from "@/lib/formatters";
import {
  EXPENSE_CLAIM_MAX_ITEMS,
  isPositiveAmount,
  isReceiptDateAllowed,
  parseAmountInput,
} from "@/lib/requestValidation";
import { expenseCategoryLabel } from "@/lib/expenseLabels";
import { getApiErrorMessage } from "@/lib/apiError";
import i18n from "@/lib/i18n";
import { palette } from "@/theme/theme";
import { ExpenseItemList } from "./expense/ExpenseItemList";
import { requestFormStyles as styles } from "./requestFormStyles";

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

type Props = {
  /** Called after a successful submit (NewRequest closes itself with it). */
  onSubmitted?: () => void;
};

/**
 * "Masraf talebi" form (NewRequest body): items are added one by one, then the whole claim is
 * submitted. The caller's own claim history lives on Taleplerim (`RequestsScreen`).
 */
export default function ExpenseClaimSection({ onSubmitted }: Props) {
  const { t } = useTranslation("requests");
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [categorySheetVisible, setCategorySheetVisible] = useState(false);

  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [amountText, setAmountText] = useState("");
  const [receiptDate, setReceiptDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");
  const [items, setItems] = useState<DraftExpenseItem[]>([]);
  const nextLocalId = useRef(0);

  const loadCategories = useCallback(async () => {
    setError(null);
    try {
      const active = (await expenseService.getCategories()).filter((c) => c.isActive);
      setCategories(active);
      setCategoryId((current) => current ?? active[0]?.id ?? null);
    } catch {
      setError(i18n.t("requests:expense.loadError"));
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadCategories().finally(() => setLoading(false));
  }, [loadCategories]);

  function addItem() {
    const amount = parseAmountInput(amountText);
    if (!categoryId) {
      Alert.alert(t("common:alerts.error"), t("expense.selectCategory"));
      return;
    }
    if (amount === null || !isPositiveAmount(amount)) {
      Alert.alert(t("common:alerts.error"), t("advance.invalidAmount"));
      return;
    }
    if (!isReceiptDateAllowed(receiptDate)) {
      Alert.alert(t("common:alerts.error"), t("expense.receiptDateFuture"));
      return;
    }
    if (items.length >= EXPENSE_CLAIM_MAX_ITEMS) {
      Alert.alert(
        t("common:alerts.error"),
        t("expense.maxItems", { max: EXPENSE_CLAIM_MAX_ITEMS })
      );
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
      onSubmitted?.();
      Alert.alert(t("common:alerts.sent"), t("expense.submitted"));
    } catch (submitError) {
      if (submitError instanceof ExpenseClaimDraftIncompleteError) {
        Alert.alert(t("expense.draftTitle"), t("expense.draftMessage"));
      } else {
        Alert.alert(
          t("common:alerts.error"),
          getApiErrorMessage(submitError, t("expense.submitError"))
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

  const categoryName = (id: string) =>
    expenseCategoryLabel(categories.find((c) => c.id === id)?.code ?? id);
  const selectedCategory = categories.find((c) => c.id === categoryId);
  const canAddItem =
    canAddExpenseItem({ categoryId, amountText, receiptDate, itemCount: items.length }) &&
    !submitting;
  const canSubmit = canSubmitExpenseClaim({ itemCount: items.length }) && !submitting;

  return (
    <View style={styles.form}>
      {error ? <ErrorBanner message={error} onRetry={() => void loadCategories()} /> : null}

      {categories.length === 0 ? (
        <Text style={styles.hint}>{t("expense.noCategories")}</Text>
      ) : (
        <SelectField
          label={t("expense.category")}
          required
          value={selectedCategory ? expenseCategoryLabel(selectedCategory.code) : undefined}
          placeholder={t("expense.categoryPlaceholder")}
          onPress={() => setCategorySheetVisible(true)}
        />
      )}
      <TextField
        label={t("advance.amount")}
        required
        value={amountText}
        onChangeText={setAmountText}
        placeholder={t("amountPlaceholder")}
        keyboardType="decimal-pad"
      />
      <SheetDateField
        label={t("expense.receiptDate")}
        required
        value={receiptDate}
        onChange={setReceiptDate}
      />
      <TextField
        label={t("form.descriptionOptional")}
        value={description}
        onChangeText={setDescription}
        placeholder={t("expense.descriptionPlaceholder")}
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />
      <PrimaryButton
        label={t("expense.addItem")}
        variant="secondary"
        icon="add"
        onPress={addItem}
        disabled={!canAddItem}
      />

      {items.length > 0 ? (
        <ExpenseItemList
          items={items.map((item) => ({
            localId: item.localId,
            categoryLabel: categoryName(item.categoryId),
            receiptDate: item.receiptDate,
            amount: item.amount,
          }))}
          total={sumExpenseItemAmounts(items)}
          onRemove={removeItem}
          disabled={submitting}
        />
      ) : (
        <Text style={styles.hint}>{t("expense.addItemHint")}</Text>
      )}

      <View style={styles.submit}>
        <PrimaryButton
          label={t("form.submit")}
          onPress={() => void submit()}
          loading={submitting}
          disabled={!canSubmit}
        />
      </View>

      <OptionSheet
        visible={categorySheetVisible}
        title={t("expense.category")}
        options={categories.map((c) => ({ value: c.id, label: expenseCategoryLabel(c.code) }))}
        selected={categoryId}
        onSelect={setCategoryId}
        onClose={() => setCategorySheetVisible(false)}
      />
    </View>
  );
}
