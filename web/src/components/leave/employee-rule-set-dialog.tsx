import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button, Group, Modal, Select, Skeleton, Stack, Text } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import { assignLeaveRuleSet, listLeaveRuleSets, type LeaveRuleSetAssignmentResult } from "@/services/leave.service";
import { useLeaveName } from "./leave-shared";

interface ChangeEmployeeRuleSetDialogProps {
  employeeId: string;
  employeeName: string;
  currentRuleSetId: string;
  onClose: () => void;
  onChanged: (result: LeaveRuleSetAssignmentResult) => void;
}

/** Employee card "Kural setini değiştir": assigns one employee to another rule set (leave.rule_sets.assign). */
export function ChangeEmployeeRuleSetDialog({ employeeId, employeeName, currentRuleSetId, onClose, onChanged }: ChangeEmployeeRuleSetDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const [ruleSetId, setRuleSetId] = useState(currentRuleSetId);
  const [error, setError] = useState<string | null>(null);
  const setsQuery = useQuery({ queryKey: ["leave", "rule-sets"], queryFn: listLeaveRuleSets });

  const mutation = useMutation({
    mutationFn: (id: string) => assignLeaveRuleSet(id, [employeeId]),
    onSuccess: (result) => {
      if (result.assignedCount === 0 && result.warnings.length > 0) {
        setError(result.warnings.map((warning) => warning.message).join(" "));
        return;
      }
      onChanged(result);
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (ruleSetId === currentRuleSetId) return onClose();
    setError(null);
    mutation.mutate(ruleSetId);
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} size="28rem" title={t("leave:ruleSetChange.title")}>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t("leave:ruleSetChange.description", { name: employeeName })}
        </Text>
        {setsQuery.isLoading ? (
          <Skeleton h={40} />
        ) : setsQuery.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(setsQuery.error, t("leave:genericError"))}
          </Text>
        ) : (
          <form onSubmit={submit}>
            <Stack gap="md">
              <Select
                id="employee-rule-set"
                label={t("leave:summary.ruleSet")}
                description={t("leave:ruleSetChange.hint")}
                inputWrapperOrder={["label", "input", "description", "error"]}
                value={ruleSetId}
                onChange={(value) => value && setRuleSetId(value)}
                allowDeselect={false}
                disabled={busy}
                data={(setsQuery.data ?? []).map((set) => ({
                  value: set.id,
                  label: `${leaveName(set.name)}${set.isDefault ? ` (${t("leave:ruleSets.default")})` : ""}`,
                }))}
              />
              {error && (
                <Text size="sm" c="red" role="alert">
                  {error}
                </Text>
              )}
              <Group justify="flex-end" gap="xs">
                <Button type="button" variant="default" onClick={onClose} disabled={busy}>
                  {t("common:cancel")}
                </Button>
                <Button type="submit" loading={busy}>
                  {busy ? t("common:saving") : t("common:save")}
                </Button>
              </Group>
            </Stack>
          </form>
        )}
      </Stack>
    </Modal>
  );
}
