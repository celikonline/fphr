import React, { useState } from "react";
import { Alert, View } from "react-native";
import { useTranslation } from "react-i18next";
import { PrimaryButton, TextField } from "@/components/ui";
import { SheetDateField } from "@/components/form/SheetDateField";
import { expenseService, EXPENSE_DEFAULT_CURRENCY } from "@/services/ExpenseService";
import { toLocalDateOnly } from "@/lib/formatters";
import {
  isAdvanceRequestedDateAllowed,
  isPositiveAmount,
  parseAmountInput,
} from "@/lib/requestValidation";
import { getApiErrorMessage } from "@/lib/apiError";
import { requestFormStyles as styles } from "./requestFormStyles";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/** `CreateAdvanceRequestValidator`: only `Amount` (>0) and `RequestedDate` (when given) are checked client-side. */
export function canSubmitAdvanceRequest(params: {
  amountText: string;
  requestedDate: string;
}): boolean {
  const amount = parseAmountInput(params.amountText);
  if (amount === null || !isPositiveAmount(amount)) return false;
  return isAdvanceRequestedDateAllowed(params.requestedDate);
}

type Props = {
  /** Called after a successful submit (NewRequest closes itself with it). */
  onSubmitted?: () => void;
};

/**
 * "Avans talebi" form (NewRequest body). The caller's own advance history lives on Taleplerim
 * (`RequestsScreen`).
 */
export default function AdvanceRequestSection({ onSubmitted }: Props) {
  const { t } = useTranslation("requests");
  const [submitting, setSubmitting] = useState(false);
  const [amountText, setAmountText] = useState("");
  const [requestedDate, setRequestedDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");

  async function submit() {
    const amount = parseAmountInput(amountText);
    if (amount === null || !isPositiveAmount(amount)) {
      Alert.alert(t("common:alerts.error"), t("advance.invalidAmount"));
      return;
    }
    if (!isAdvanceRequestedDateAllowed(requestedDate)) {
      Alert.alert(t("common:alerts.error"), t("advance.invalidDate"));
      return;
    }

    setSubmitting(true);
    try {
      await expenseService.createAdvanceRequest({
        amount,
        currency: EXPENSE_DEFAULT_CURRENCY,
        requestedDate,
        isInstallmentRequested: false,
        description: description.trim().length > 0 ? description.trim() : null,
      });
      setAmountText("");
      setDescription("");
      setRequestedDate(todayDateOnly());
      onSubmitted?.();
      Alert.alert(t("common:alerts.sent"), t("advance.submitted"));
    } catch (submitError) {
      Alert.alert(
        t("common:alerts.error"),
        getApiErrorMessage(submitError, t("advance.submitError"))
      );
    } finally {
      setSubmitting(false);
    }
  }

  const canSubmit = canSubmitAdvanceRequest({ amountText, requestedDate }) && !submitting;

  return (
    <View style={styles.form}>
      <TextField
        label={t("advance.amount")}
        required
        value={amountText}
        onChangeText={setAmountText}
        placeholder={t("amountPlaceholder")}
        keyboardType="decimal-pad"
      />
      <SheetDateField
        label={t("advance.requestedDate")}
        required
        value={requestedDate}
        onChange={setRequestedDate}
      />
      <TextField
        label={t("form.descriptionOptional")}
        value={description}
        onChangeText={setDescription}
        placeholder={t("advance.descriptionPlaceholder")}
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />
      <View style={styles.submit}>
        <PrimaryButton
          label={t("form.submit")}
          onPress={() => void submit()}
          loading={submitting}
          disabled={!canSubmit}
        />
      </View>
    </View>
  );
}
