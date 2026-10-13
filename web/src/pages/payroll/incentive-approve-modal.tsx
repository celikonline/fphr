import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Group, Modal, Stack, Text, Textarea } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import { approveIncentiveDefinition, revokeIncentiveApproval, type IncentiveDefinition } from "@/services/payroll-incentives.service";
import { INCENTIVES_QUERY_KEY } from "./incentive-form";

const NOTE_MAX_LENGTH = 500;

export type IncentiveApprovalMode = "approve" | "revoke";

interface IncentiveApproveModalProps {
  opened: boolean;
  onClose: () => void;
  mode: IncentiveApprovalMode;
  item: IncentiveDefinition | null;
}

/**
 * SMMM/expert approval (or its revocation) with a note. Approval is a declaration flag: who, when and the note are recorded,
 * but the correctness of the rates stays with the tenant. Server messages (incomplete draft, unknown legislation key) are shown as is.
 */
export function IncentiveApproveModal({ opened, onClose, mode, item }: IncentiveApproveModalProps) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Modal
      opened={opened}
      onClose={onClose}
      size="34rem"
      title={mode === "approve" ? t("payroll:incentives.approveModal.approveTitle", { code: item?.code ?? "" }) : t("payroll:incentives.approveModal.revokeTitle", { code: item?.code ?? "" })}
    >
      {opened && item && <ApprovalBody key={`${item.id}:${mode}`} mode={mode} item={item} onClose={onClose} />}
    </Modal>
  );
}

function ApprovalBody({ mode, item, onClose }: { mode: IncentiveApprovalMode; item: IncentiveDefinition; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const approving = mode === "approve";

  const submit = useMutation({
    mutationFn: () => (approving ? approveIncentiveDefinition(item.id, note.trim()) : revokeIncentiveApproval(item.id, note.trim())),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: INCENTIVES_QUERY_KEY });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t(approving ? "payroll:incentives.approveModal.approveError" : "payroll:incentives.approveModal.revokeError"))),
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        submit.mutate();
      }}
    >
      <Stack gap="sm">
        <Text size="sm" fw={500}>
          {item.code} · {item.name}
        </Text>
        {approving ? (
          <Alert color="blue" data-testid="incentive-approval-disclaimer">
            {t("payroll:incentives.approveModal.disclaimer")}
          </Alert>
        ) : (
          <Text size="sm" c="dimmed">
            {t("payroll:incentives.approveModal.revokeHint")}
          </Text>
        )}
        {approving && item.isComplete === false && (
          <Text size="xs" c="orange" data-testid="incentive-approval-incomplete">
            {t("payroll:incentives.approveModal.incompleteHint")}
          </Text>
        )}
        <Textarea
          label={t("payroll:incentives.approveModal.note")}
          description={t("payroll:incentives.approveModal.noteHint")}
          value={note}
          onChange={(event) => setNote(event.currentTarget.value)}
          maxLength={NOTE_MAX_LENGTH}
          rows={3}
        />
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" color={approving ? "green" : "red"} loading={submit.isPending}>
            {approving ? t("payroll:incentives.approveModal.confirmApprove") : t("payroll:incentives.approveModal.confirmRevoke")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
