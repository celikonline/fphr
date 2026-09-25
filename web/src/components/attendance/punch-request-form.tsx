import { type FormEvent, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button, Group, SegmentedControl, Stack, Textarea, TextInput } from "@mantine/core";
import { FormModal } from "@/components/ui/form-modal";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import { createPunchRequest } from "@/services/attendance-punch-requests.service";

const REASON_MAX_LENGTH = 500;

type Direction = "in" | "out";
type FormError = "required" | "futureBlocked" | "reasonRequired" | null;

export const MY_PUNCH_REQUESTS_KEY = ["attendance", "punch-requests", "me"] as const;

interface PunchRequestFormProps {
  opened: boolean;
  onClose: () => void;
}

/** Eksik kayıt talebi formu (Z2.1): yerel tarih-saat seçilir, UTC anına çevrilip gönderilir; gelecek zaman engellenir. */
export function PunchRequestForm({ opened, onClose }: PunchRequestFormProps) {
  const { t } = useTranslation(["attendance-daily", "common"]);
  const queryClient = useQueryClient();
  const [date, setDate] = useState("");
  const [time, setTime] = useState("");
  const [direction, setDirection] = useState<Direction>("in");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<FormError>(null);

  const mutation = useMutation({
    mutationFn: createPunchRequest,
    onSuccess: async () => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance-daily:punchRequests.submitted") });
      await queryClient.invalidateQueries({ queryKey: ["attendance", "punch-requests"] });
      setDate("");
      setTime("");
      setReason("");
      onClose();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("common:error")) }),
  });

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!date || !time) return setError("required");
    if (!reason.trim()) return setError("reasonRequired");
    const [year, month, day] = date.split("-").map(Number);
    const [hour, minute] = time.split(":").map(Number);
    const occurredAt = new Date(year, month - 1, day, hour, minute);
    if (Number.isNaN(occurredAt.getTime())) return setError("required");
    if (occurredAt.getTime() > Date.now()) return setError("futureBlocked");
    setError(null);
    mutation.mutate({ occurredAtUtc: occurredAt.toISOString(), direction, reason: reason.trim() });
  };

  return (
    <FormModal opened={opened} onClose={onClose} title={t("attendance-daily:punchRequests.formTitle")}>
      <form onSubmit={handleSubmit} noValidate>
        <Stack gap="sm">
          <Group grow align="flex-start">
            <TextInput
              type="date"
              label={t("attendance-daily:punchRequests.date")}
              value={date}
              max={toDateOnly(new Date())}
              onChange={(e) => setDate(e.currentTarget.value)}
              withAsterisk
            />
            <TextInput
              type="time"
              label={t("attendance-daily:punchRequests.time")}
              value={time}
              onChange={(e) => setTime(e.currentTarget.value)}
              withAsterisk
            />
          </Group>
          <SegmentedControl
            fullWidth
            aria-label={t("attendance-daily:punchRequests.direction")}
            value={direction}
            onChange={(value) => setDirection(value as Direction)}
            data={[
              { value: "in", label: t("attendance-daily:punchRequests.in") },
              { value: "out", label: t("attendance-daily:punchRequests.out") },
            ]}
          />
          <Textarea
            label={t("attendance-daily:punchRequests.reason")}
            placeholder={t("attendance-daily:punchRequests.reasonPlaceholder")}
            value={reason}
            maxLength={REASON_MAX_LENGTH}
            rows={3}
            onChange={(e) => setReason(e.currentTarget.value)}
            withAsterisk
          />
          {error && (
            <div role="alert" className="text-sm text-red-600">
              {t(`attendance-daily:punchRequests.${error}`)}
            </div>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              {t("attendance-daily:punchRequests.cancelForm")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {t("attendance-daily:punchRequests.submit")}
            </Button>
          </Group>
        </Stack>
      </form>
    </FormModal>
  );
}
