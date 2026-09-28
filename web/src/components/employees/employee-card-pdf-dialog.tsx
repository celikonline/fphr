import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Checkbox, Group, Input, Modal, SegmentedControl, SimpleGrid, Stack, Text } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { useVisibleEmployeeCardTabs } from "@/components/employees/card-tabs";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { saveDownloadedFile } from "@/services/data-transfer.service";
import { downloadEmployeeCardPdf } from "@/services/employees.service";

export type CardPdfPeriodPreset = "last12Months" | "thisYear" | "allTime" | "custom";

const PRESETS: CardPdfPeriodPreset[] = ["last12Months", "thisYear", "allTime", "custom"];
/** Tabs with no printable content of their own (shortcuts only). */
const EXCLUDED_TABS = new Set(["other"]);

function toDateKey(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Period sent to the API for a preset; `null` when a custom range is incomplete or reversed. */
export function cardPdfPeriod(
  preset: CardPdfPeriodPreset,
  today: Date,
  custom: { from: string; to: string }
): { from?: string; to?: string } | null {
  const to = toDateKey(today);
  if (preset === "allTime") return {};
  if (preset === "thisYear") return { from: `${today.getFullYear()}-01-01`, to };
  if (preset === "last12Months") {
    const from = new Date(today);
    from.setFullYear(from.getFullYear() - 1);
    return { from: toDateKey(from), to };
  }
  if (!custom.from || !custom.to || custom.from > custom.to) return null;
  return { from: custom.from, to: custom.to };
}

interface EmployeeCardPdfDialogProps {
  employeeId: string;
  isOwnCard: boolean;
  onClose: () => void;
}

export function EmployeeCardPdfDialog({ employeeId, isOwnCard, onClose }: EmployeeCardPdfDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const tabs = useVisibleEmployeeCardTabs().filter((tab) => !EXCLUDED_TABS.has(tab.key));
  const [selected, setSelected] = useState<string[]>(() => tabs.map((tab) => tab.key));
  const [preset, setPreset] = useState<CardPdfPeriodPreset>("last12Months");
  const [custom, setCustom] = useState({ from: "", to: "" });
  const period = cardPdfPeriod(preset, new Date(), custom);
  const allSelected = selected.length === tabs.length;
  const customInvalid = preset === "custom" && period === null && Boolean(custom.from && custom.to);

  const mutation = useMutation({
    mutationFn: () =>
      downloadEmployeeCardPdf(isOwnCard ? "me" : employeeId, {
        // Card order, not click order.
        sections: tabs.map((tab) => tab.key).filter((key) => selected.includes(key)),
        ...period,
      }),
    onSuccess: (file) => {
      saveDownloadedFile(file);
      onClose();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("employees:card.pdf.failed")),
      }),
  });

  return (
    <Modal opened onClose={onClose} title={t("employees:card.pdf.title")} size="lg">
      <Stack gap="md">
        <Group justify="space-between">
          <Text size="sm" fw={500}>
            {t("employees:card.pdf.sections")}
          </Text>
          <Button variant="subtle" size="xs" onClick={() => setSelected(allSelected ? [] : tabs.map((tab) => tab.key))}>
            {allSelected ? t("employees:card.pdf.clearAll") : t("employees:card.pdf.selectAll")}
          </Button>
        </Group>
        <Checkbox.Group value={selected} onChange={setSelected}>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
            {tabs.map((tab) => (
              <Checkbox key={tab.key} value={tab.key} label={t(tab.labelKey)} />
            ))}
          </SimpleGrid>
        </Checkbox.Group>

        <Input.Wrapper label={t("employees:card.pdf.period")} description={t("employees:card.pdf.periodHint")}>
          <SegmentedControl
            fullWidth
            mt={4}
            value={preset}
            onChange={(value) => setPreset(value as CardPdfPeriodPreset)}
            data={PRESETS.map((value) => ({ value, label: t(`employees:card.pdf.presets.${value}`) }))}
          />
        </Input.Wrapper>
        {preset === "custom" && (
          <SimpleGrid cols={2} spacing="xs">
            <Input.Wrapper label={t("employees:card.pdf.from")}>
              <DatePicker
                value={custom.from}
                max={custom.to || undefined}
                onChange={(from) => setCustom((current) => ({ ...current, from }))}
              />
            </Input.Wrapper>
            <Input.Wrapper label={t("employees:card.pdf.to")} error={customInvalid ? t("employees:card.pdf.invalidPeriod") : undefined}>
              <DatePicker
                value={custom.to}
                min={custom.from || undefined}
                invalid={customInvalid}
                onChange={(to) => setCustom((current) => ({ ...current, to }))}
              />
            </Input.Wrapper>
          </SimpleGrid>
        )}

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button onClick={() => mutation.mutate()} loading={mutation.isPending} disabled={selected.length === 0 || period === null}>
            {t("employees:card.pdf.generate")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
