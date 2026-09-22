import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Group, Modal, Text, Textarea } from "@mantine/core";
import { CheckCircle2, Lock, RotateCcw, Wallet } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  approvePayrollPeriod,
  lockPayrollPeriod,
  markPayrollPeriodPaid,
  reopenPayrollPeriod,
  type PayrollPeriodStatus,
} from "@/services/payroll.service";

type PendingAction = "approve" | "markPaid" | "lock" | "reopen";

const REASON_MAX_LENGTH = 500;

/**
 * Close-out actions of a payroll period: Onayla → Ödendi işaretle → Kilitle, and the audited "Yeniden aç" (written reason).
 * Each button shows only for the status it applies to and when the user holds the matching permission; the backend enforces both again.
 */
export function PeriodActions({
  periodId,
  status,
}: {
  periodId: string;
  status: PayrollPeriodStatus;
}) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const canApprove = usePermission("payroll.period.approve");
  const canLock = usePermission("payroll.period.lock");
  const canReopen = usePermission("payroll.period.reopen");
  const [pending, setPending] = useState<PendingAction | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const close = () => {
    setPending(null);
    setReason("");
    setError(null);
  };

  const mutation = useMutation({
    mutationFn: async (action: PendingAction) => {
      switch (action) {
        case "approve":
          return approvePayrollPeriod(periodId);
        case "markPaid":
          return markPayrollPeriodPaid(periodId);
        case "lock":
          return lockPayrollPeriod(periodId);
        case "reopen":
          return reopenPayrollPeriod(periodId, reason.trim());
      }
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("payroll:periodDetail.actions.success"),
      });
      close();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:periodDetail.actions.error"))),
  });

  const canReopenNow = canReopen && status !== "open";

  return (
    <>
      <Group gap="xs" wrap="wrap">
        {status === "open" && canApprove && (
          <Button leftSection={<CheckCircle2 size={16} />} onClick={() => setPending("approve")}>
            {t("payroll:periodDetail.actions.approve")}
          </Button>
        )}
        {status === "approved" && canApprove && (
          <Button leftSection={<Wallet size={16} />} onClick={() => setPending("markPaid")}>
            {t("payroll:periodDetail.actions.markPaid")}
          </Button>
        )}
        {status === "paid" && canLock && (
          <Button leftSection={<Lock size={16} />} onClick={() => setPending("lock")}>
            {t("payroll:periodDetail.actions.lock")}
          </Button>
        )}
        {canReopenNow && (
          <Button
            variant="default"
            color="red"
            leftSection={<RotateCcw size={16} />}
            onClick={() => setPending("reopen")}
          >
            {t("payroll:periodDetail.actions.reopen")}
          </Button>
        )}
      </Group>

      <Modal
        opened={pending !== null}
        onClose={close}
        title={pending ? t(`payroll:periodDetail.actions.${pending}`) : ""}
        size="30rem"
      >
        {pending && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              setError(null);
              mutation.mutate(pending);
            }}
          >
            <Text size="sm" mb="sm">
              {t(`payroll:periodDetail.actions.confirm.${pending}`)}
            </Text>
            {pending === "reopen" && (
              <Textarea
                label={t("payroll:periodDetail.actions.reason")}
                value={reason}
                onChange={(event) => setReason(event.currentTarget.value)}
                maxLength={REASON_MAX_LENGTH}
                minRows={3}
                autosize
                required
                data-autofocus
              />
            )}
            {error && (
              <Alert color="red" mt="sm">
                {error}
              </Alert>
            )}
            <Group justify="flex-end" mt="md">
              <Button variant="default" onClick={close}>
                {t("common:cancel")}
              </Button>
              <Button
                type="submit"
                color={pending === "reopen" ? "red" : undefined}
                loading={mutation.isPending}
                disabled={pending === "reopen" && reason.trim().length === 0}
              >
                {t(`payroll:periodDetail.actions.${pending}`)}
              </Button>
            </Group>
          </form>
        )}
      </Modal>
    </>
  );
}
