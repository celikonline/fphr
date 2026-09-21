import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Checkbox, Group, Modal, NumberInput, Select, Stack, Text, TextInput } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  addExitSettlementLine,
  MANUAL_EXIT_PAY_KINDS,
  updateExitSettlementLine,
  type ExitPayKind,
  type ExitSettlement,
  type ExitSettlementLine,
} from "@/services/payroll-severance.service";

/** Default tax flags per kind, mirroring `ExitSettlementNetCalculator.DefaultTreatment` on the backend. */
function defaultTaxes(kind: ExitPayKind) {
  switch (kind) {
    case "severance":
      return { sgkSubject: false, incomeTaxSubject: false, stampTaxSubject: true };
    case "unusedLeave":
    case "finalMonthWage":
      return { sgkSubject: true, incomeTaxSubject: true, stampTaxSubject: true };
    default:
      return { sgkSubject: false, incomeTaxSubject: true, stampTaxSubject: true };
  }
}

interface Props {
  settlement: ExitSettlement;
  /** The line being edited, or null to add a manual one. */
  line: ExitSettlementLine | null;
  onClose: () => void;
}

/** Adds a manual exit-settlement line (ikale bedeli, ek ödeme, çıkış ayı ücreti...) or edits any line's name, amount and tax flags. */
export function ExitSettlementLineDialog({ settlement, line, onClose }: Props) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<ExitPayKind>(line?.kind ?? "mutualRescissionPay");
  const [label, setLabel] = useState(line?.label ?? "");
  const [grossAmount, setGrossAmount] = useState<number | string>(line?.grossAmount ?? "");
  const [taxes, setTaxes] = useState(
    line
      ? { sgkSubject: line.sgkSubject, incomeTaxSubject: line.incomeTaxSubject, stampTaxSubject: line.stampTaxSubject }
      : defaultTaxes("mutualRescissionPay"),
  );
  const [error, setError] = useState<string | null>(null);

  const isAutomatic = line?.isAutomatic ?? false;
  const amount = Number(grossAmount);
  const canSubmit = amount > 0 && (isAutomatic || label.trim().length > 0);

  const mutation = useMutation({
    mutationFn: async () => {
      const input = { label: label.trim(), grossAmount: amount, ...taxes };
      if (line) {
        await updateExitSettlementLine(settlement.id, line.id, input);
      } else {
        await addExitSettlementLine(settlement.id, kind, input);
      }
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["exit-settlement", settlement.employeeId] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:exitSettlement.success") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  const changeKind = (value: string | null) => {
    if (!value) {
      return;
    }
    const next = value as ExitPayKind;
    setKind(next);
    setTaxes(defaultTaxes(next));
  };

  return (
    <Modal opened onClose={onClose} title={t(line ? "employees:exitSettlement.dialog.titleEdit" : "employees:exitSettlement.dialog.titleAdd")} size="30rem">
      <form
        onSubmit={(event: FormEvent) => {
          event.preventDefault();
          setError(null);
          mutation.mutate();
        }}
      >
        <Stack gap="sm">
          {line ? (
            <Text size="sm" fw={500}>
              {t(`employees:exitSettlement.kinds.${line.kind}`)}
            </Text>
          ) : (
            <Select
              label={t("employees:exitSettlement.dialog.kind")}
              data={MANUAL_EXIT_PAY_KINDS.map((value) => ({ value, label: t(`employees:exitSettlement.kinds.${value}`) }))}
              value={kind}
              onChange={changeKind}
              allowDeselect={false}
            />
          )}
          <TextInput
            label={t("employees:exitSettlement.dialog.label")}
            value={label}
            onChange={(event) => setLabel(event.currentTarget.value)}
            maxLength={200}
            required={!isAutomatic}
          />
          <NumberInput
            label={t("employees:exitSettlement.dialog.grossAmount")}
            value={grossAmount}
            onChange={setGrossAmount}
            min={0}
            decimalScale={2}
            thousandSeparator="."
            decimalSeparator=","
            required
          />
          <Stack gap={6}>
            <Text size="sm" fw={500}>
              {t("employees:exitSettlement.dialog.taxes")}
            </Text>
            <Checkbox
              label={t("employees:exitSettlement.dialog.sgkSubject")}
              checked={taxes.sgkSubject}
              onChange={(event) => setTaxes({ ...taxes, sgkSubject: event.currentTarget.checked })}
            />
            <Checkbox
              label={t("employees:exitSettlement.dialog.incomeTaxSubject")}
              checked={taxes.incomeTaxSubject}
              onChange={(event) => setTaxes({ ...taxes, incomeTaxSubject: event.currentTarget.checked })}
            />
            <Checkbox
              label={t("employees:exitSettlement.dialog.stampTaxSubject")}
              checked={taxes.stampTaxSubject}
              onChange={(event) => setTaxes({ ...taxes, stampTaxSubject: event.currentTarget.checked })}
            />
          </Stack>
          {error && <Alert color="red">{error}</Alert>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={onClose}>
              {t("employees:exitSettlement.dialog.cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending} disabled={!canSubmit}>
              {t("employees:exitSettlement.dialog.save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
