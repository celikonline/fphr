import React, { useState } from "react";
import { Alert, View } from "react-native";
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
  const [submitting, setSubmitting] = useState(false);
  const [amountText, setAmountText] = useState("");
  const [requestedDate, setRequestedDate] = useState(todayDateOnly());
  const [description, setDescription] = useState("");

  async function submit() {
    const amount = parseAmountInput(amountText);
    if (amount === null || !isPositiveAmount(amount)) {
      Alert.alert("Hata", "Lütfen geçerli, sıfırdan büyük bir tutar girin.");
      return;
    }
    if (!isAdvanceRequestedDateAllowed(requestedDate)) {
      Alert.alert("Hata", "Talep tarihi izin verilen aralığın dışında.");
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
      Alert.alert("Gönderildi", "Avans talebiniz onaya gönderildi.");
    } catch (submitError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(submitError, "Avans talebi gönderilemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setSubmitting(false);
    }
  }

  const canSubmit = canSubmitAdvanceRequest({ amountText, requestedDate }) && !submitting;

  return (
    <View style={styles.form}>
      <TextField
        label="Tutar (TRY)"
        required
        value={amountText}
        onChangeText={setAmountText}
        placeholder="0,00"
        keyboardType="decimal-pad"
      />
      <SheetDateField
        label="Talep tarihi"
        required
        value={requestedDate}
        onChange={setRequestedDate}
      />
      <TextField
        label="Açıklama (opsiyonel)"
        value={description}
        onChangeText={setDescription}
        placeholder="Örn. yol masrafı için avans"
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
    </View>
  );
}
