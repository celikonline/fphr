import { useState } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Group, Modal, NumberInput, Select, Stack } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import { openPayrollPeriod } from "@/services/payroll.service";

/** API: `PayrollLimits.MinPeriodYear` ile (bu yıl + 1) arası kabul edilir (`OpenPayrollPeriodHandler`). */
const MIN_YEAR = 2024;
const MONTHS = Array.from({ length: 12 }, (_, index) => index + 1);

interface PeriodOpenModalProps {
  opened: boolean;
  onClose: () => void;
}

/**
 * "Dönem aç": yıl + ay seçtirir (varsayılan içinde bulunulan ay), `POST /payroll/periods` çağırır,
 * dönem listesini yeniler ve yeni dönemin detayına gider. Sunucu hataları (ör. dönem zaten açık) modalda gösterilir.
 * Form modal içeriği olarak bağlanır; Modal kapanınca içerik söküldüğü için her açılış içinde bulunulan aya sıfırlanır.
 */
export function PeriodOpenModal({ opened, onClose }: PeriodOpenModalProps) {
  const { t } = useTranslation("payroll");
  return (
    <Modal opened={opened} onClose={onClose} title={t("periods.openDialog.title")} size="26rem">
      <PeriodOpenForm onClose={onClose} />
    </Modal>
  );
}

function PeriodOpenForm({ onClose }: { onClose: () => void }) {
  const { t, i18n } = useTranslation(["payroll", "common"]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const maxYear = new Date().getFullYear() + 1;
  const [year, setYear] = useState<number | string>(() => new Date().getFullYear());
  const [month, setMonth] = useState<string | null>(() => String(new Date().getMonth() + 1));
  const [error, setError] = useState<string | null>(null);

  const monthOptions = MONTHS.map((value) => ({
    value: String(value),
    label: new Intl.DateTimeFormat(i18n.language, { month: "long" }).format(new Date(2000, value - 1, 1)),
  }));

  const mutation = useMutation({
    mutationFn: ({ y, m }: { y: number; m: number }) => openPayrollPeriod(y, m),
    onSuccess: async (periodId) => {
      await queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] });
      onClose();
      void navigate(`/app/payroll/periods/${periodId}`);
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:periods.openDialog.error"))),
  });

  const yearValue = typeof year === "number" ? year : Number.NaN;
  const canSubmit = Number.isInteger(yearValue) && yearValue >= MIN_YEAR && yearValue <= maxYear && month !== null;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!canSubmit) return;
        setError(null);
        mutation.mutate({ y: yearValue, m: Number(month) });
      }}
    >
      <Stack gap="sm">
        <NumberInput
          label={t("payroll:periods.openDialog.year")}
          value={year}
          onChange={setYear}
          min={MIN_YEAR}
          max={maxYear}
          allowDecimal={false}
          allowNegative={false}
          required
          data-autofocus
        />
        <Select
          label={t("payroll:periods.openDialog.month")}
          data={monthOptions}
          value={month}
          onChange={setMonth}
          allowDeselect={false}
          required
        />
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={mutation.isPending} disabled={!canSubmit}>
            {t("payroll:periods.openDialog.submit")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
