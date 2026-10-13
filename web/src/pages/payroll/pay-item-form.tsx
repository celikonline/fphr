import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Checkbox, Group, Modal, NumberInput, SegmentedControl, Select, Stack, Text, TextInput } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createPayItem,
  updatePayItem,
  type PayItemAmountBasis,
  type PayItemDefinition,
  type PayItemDirection,
  type PayItemInput,
} from "@/services/payroll-pay-items.service";
import { FormulaEditor } from "./formula-editor";

export const PAY_ITEMS_QUERY_KEY = ["payroll", "pay-items"] as const;

interface PayItemFormProps {
  opened: boolean;
  onClose: () => void;
  /** Present when editing; absent when creating. */
  item?: PayItemDefinition | null;
}

/** Create/edit modal. The code is chosen once and locked afterwards; a net item is always fully taxable (flags forced on). */
export function PayItemForm({ opened, onClose, item }: PayItemFormProps) {
  // Remount per open/item so the local form state always starts from the right values.
  return (
    <Modal opened={opened} onClose={onClose} size="42rem" title={<FormTitle editing={Boolean(item)} />}>
      {opened && <PayItemFormBody key={item?.id ?? "new"} item={item ?? null} onClose={onClose} />}
    </Modal>
  );
}

function FormTitle({ editing }: { editing: boolean }) {
  const { t } = useTranslation(["payroll"]);
  return <>{editing ? t("payroll:payItems.form.editTitle") : t("payroll:payItems.form.createTitle")}</>;
}

function PayItemFormBody({ item, onClose }: { item: PayItemDefinition | null; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const editing = item !== null;

  const [code, setCode] = useState(item?.code ?? "");
  const [name, setName] = useState(item?.name ?? "");
  const [direction, setDirection] = useState<PayItemDirection>(item?.direction ?? "earning");
  const [amountBasis, setAmountBasis] = useState<PayItemAmountBasis>(item?.amountBasis ?? "gross");
  const [sgk, setSgk] = useState(item?.isSgkSubject ?? true);
  const [incomeTax, setIncomeTax] = useState(item?.isIncomeTaxSubject ?? true);
  const [stampTax, setStampTax] = useState(item?.isStampTaxSubject ?? true);
  const [formula, setFormula] = useState(item?.formula ?? "");
  const [defaultValue, setDefaultValue] = useState<number | string>(item?.defaultValue ?? "");
  const [validFrom, setValidFrom] = useState(item?.validFrom ?? "");
  const [validTo, setValidTo] = useState(item?.validTo ?? "");
  const [error, setError] = useState<string | null>(null);

  const isDeduction = direction === "deduction";
  // A deduction is taken from net pay: always gross-basis with no tax flags (the server normalizes them too).
  const effectiveBasis: PayItemAmountBasis = isDeduction ? "gross" : amountBasis;
  const isNet = effectiveBasis === "net";

  const save = useMutation({
    mutationFn: () => {
      const input: PayItemInput = {
        name: name.trim(),
        direction,
        amountBasis: effectiveBasis,
        isSgkSubject: isDeduction ? false : isNet ? true : sgk,
        isIncomeTaxSubject: isDeduction ? false : isNet ? true : incomeTax,
        isStampTaxSubject: isDeduction ? false : isNet ? true : stampTax,
        formula: formula.trim(),
        defaultValue: defaultValue === "" ? null : Number(defaultValue),
        validFrom: validFrom || null,
        validTo: validTo || null,
      };
      return item ? updatePayItem(item.id, input) : createPayItem({ ...input, code: code.trim() }).then(() => undefined);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: PAY_ITEMS_QUERY_KEY });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:payItems.form.error"))),
  });

  const canSubmit = (editing || code.trim().length > 0) && name.trim().length > 0 && formula.trim().length > 0;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        save.mutate();
      }}
    >
      <Stack gap="sm">
        <TextInput
          label={t("payroll:payItems.form.code")}
          description={editing ? t("payroll:payItems.form.codeLocked") : t("payroll:payItems.form.codeHint")}
          value={code}
          onChange={(event) => setCode(event.currentTarget.value.toUpperCase())}
          disabled={editing}
          maxLength={32}
          required
        />
        <TextInput label={t("payroll:payItems.form.name")} value={name} onChange={(event) => setName(event.currentTarget.value)} maxLength={100} required />

        <Select
          label={t("payroll:payItems.form.direction")}
          description={editing ? t("payroll:payItems.directionLockedHint") : t("payroll:payItems.form.directionHint")}
          data={[
            { value: "earning", label: t("payroll:payItems.direction.earning") },
            { value: "deduction", label: t("payroll:payItems.direction.deduction") },
          ]}
          value={direction}
          onChange={(value) => setDirection(value === "deduction" ? "deduction" : "earning")}
          allowDeselect={false}
        />

        {isDeduction ? (
          <Text size="sm" c="dimmed" data-testid="deduction-note">
            {t("payroll:payItems.deductionNote")}
          </Text>
        ) : (
          <>
            <div>
              <Text size="sm" fw={500} mb={4}>
                {t("payroll:payItems.form.amountBasis")}
              </Text>
              <SegmentedControl
                value={amountBasis}
                onChange={(value) => setAmountBasis(value as PayItemAmountBasis)}
                data={[
                  { value: "gross", label: t("payroll:payItems.basis.gross") },
                  { value: "net", label: t("payroll:payItems.basis.net") },
                ]}
              />
            </div>

            <div>
              <Text size="sm" fw={500} mb={4}>
                {t("payroll:payItems.form.taxFlags")}
              </Text>
              <Group gap="lg">
                <Checkbox label={t("payroll:payItems.flags.sgk")} checked={isNet || sgk} disabled={isNet} onChange={(event) => setSgk(event.currentTarget.checked)} />
                <Checkbox
                  label={t("payroll:payItems.flags.incomeTax")}
                  checked={isNet || incomeTax}
                  disabled={isNet}
                  onChange={(event) => setIncomeTax(event.currentTarget.checked)}
                />
                <Checkbox
                  label={t("payroll:payItems.flags.stampTax")}
                  checked={isNet || stampTax}
                  disabled={isNet}
                  onChange={(event) => setStampTax(event.currentTarget.checked)}
                />
              </Group>
              {isNet && (
                <Text size="xs" c="dimmed" mt={4}>
                  {t("payroll:payItems.form.netFlagsLocked")}
                </Text>
              )}
            </div>
          </>
        )}

        <FormulaEditor value={formula} onChange={setFormula} amountBasis={effectiveBasis} />

        <NumberInput
          label={t("payroll:payItems.form.defaultValue")}
          description={t("payroll:payItems.form.defaultValueHint")}
          value={defaultValue}
          onChange={setDefaultValue}
          decimalScale={4}
          thousandSeparator="."
          decimalSeparator=","
        />

        <Group grow align="flex-start">
          <TextInput type="date" label={t("payroll:payItems.form.validFrom")} value={validFrom} onChange={(event) => setValidFrom(event.currentTarget.value)} />
          <TextInput type="date" label={t("payroll:payItems.form.validTo")} value={validTo} onChange={(event) => setValidTo(event.currentTarget.value)} />
        </Group>

        {error && <Alert color="red">{error}</Alert>}

        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={!canSubmit}>
            {t("payroll:payItems.form.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
