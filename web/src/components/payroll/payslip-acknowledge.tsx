import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button, Group, Modal, Stack, Textarea } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  acknowledgePayslip,
  canAcknowledge,
  canObject,
  objectToPayslip,
  type MyPayslip,
} from "@/services/payslip-ack.service";

/** "Onayla" (yalnız yayınlanmış + onaysız) ve "İtiraz et" (gerekçe zorunlu) eylemleri. */
export function PayslipAcknowledge({ payslip }: { payslip: MyPayslip }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const [opened, setOpened] = useState(false);
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["my-payslips"] });
  const onError = (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("payroll:myPayslips.actionError")),
    });

  const acknowledge = useMutation({
    mutationFn: () => acknowledgePayslip(payslip.payslipId),
    onSuccess: refresh,
    onError,
  });
  const object = useMutation({
    mutationFn: (text: string) => objectToPayslip(payslip.payslipId, text),
    onSuccess: () => {
      setOpened(false);
      setReason("");
      setTouched(false);
      return refresh();
    },
    onError,
  });

  const reasonMissing = reason.trim().length === 0;

  return (
    <>
      <Group gap="xs" justify="flex-end" wrap="nowrap">
        <Button
          size="xs"
          disabled={!canAcknowledge(payslip)}
          loading={acknowledge.isPending}
          onClick={() => acknowledge.mutate()}
        >
          {t("payroll:myPayslips.acknowledge")}
        </Button>
        <Button
          size="xs"
          variant="default"
          disabled={!canObject(payslip)}
          onClick={() => setOpened(true)}
        >
          {t("payroll:myPayslips.object")}
        </Button>
      </Group>
      <Modal opened={opened} onClose={() => setOpened(false)} title={t("payroll:myPayslips.objectTitle")}>
        <Stack>
          <Textarea
            label={t("payroll:myPayslips.reasonLabel")}
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
            onBlur={() => setTouched(true)}
            error={touched && reasonMissing ? t("payroll:myPayslips.reasonRequired") : undefined}
            minRows={3}
            required
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setOpened(false)}>
              {t("common:cancel")}
            </Button>
            <Button
              loading={object.isPending}
              onClick={() => {
                setTouched(true);
                if (!reasonMissing) object.mutate(reason.trim());
              }}
            >
              {t("payroll:myPayslips.submitObjection")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
