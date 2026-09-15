import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Badge, Button, Group, Input, Menu, Modal, Stack, Text } from "@mantine/core";
import { ChevronDown, ListChecks } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { useAuthStore } from "@/store/auth.store";
import type { BulkOperationResult, EmployeeSelection } from "@/types";
import { BULK_ACTIONS, type BulkActionDefinition, type BulkActionInput } from "./bulk-actions";
import BulkResultDialog from "./bulk-result-dialog";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";

interface BulkActionsMenuProps {
  selection: EmployeeSelection | null;
  selectedCount: number;
  nameById: ReadonlyMap<string, string>;
  /** Called after a run finishes (grid refresh + selection reset). */
  onCompleted: () => void;
}

/** "Toplu işlemler → İşlem seç": renders the registry, collects input/confirmation, runs and summarizes the outcome. */
export default function BulkActionsMenu({ selection, selectedCount, nameById, onCompleted }: BulkActionsMenuProps) {
  const { t } = useTranslation(["employees", "common"]);
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const actions = BULK_ACTIONS.filter((action) => hasPermission(action.permission));

  const [pending, setPending] = useState<BulkActionDefinition | null>(null);
  const [manager, setManager] = useState<PickedEmployee | null>(null);
  const [result, setResult] = useState<{ action: BulkActionDefinition; value: BulkOperationResult } | null>(null);
  const [resultNames, setResultNames] = useState<ReadonlyMap<string, string>>(new Map());

  const mutation = useMutation({
    mutationFn: ({ action, input }: { action: BulkActionDefinition; input: BulkActionInput }) => action.run!(selection!, input),
    onSuccess: (value, { action }) => {
      setResultNames(new Map(nameById));
      setPending(null);
      setManager(null);
      setResult({ action, value });
      onCompleted();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) }),
  });

  if (actions.length === 0) return null;

  const disabled = !selection || selectedCount === 0;

  return (
    <>
      <Menu position="bottom-start" width={256}>
        <Menu.Target>
          <Button
            type="button"
            variant="default"
            disabled={disabled}
            leftSection={<ListChecks size={16} />}
            rightSection={<ChevronDown size={16} />}
          >
            {t("employees:bulk.menu")}
          </Button>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Label>{t("employees:bulk.chooseAction")}</Menu.Label>
          <Menu.Divider />
          {actions.map((action) => (
            <Menu.Item
              key={action.code}
              disabled={action.phase !== undefined || !action.run}
              onClick={() => setPending(action)}
              rightSection={
                action.phase !== undefined && (
                  <Badge variant="outline" size="sm">
                    {t("employees:phase", { phase: action.phase })}
                  </Badge>
                )
              }
            >
              {t(`employees:${action.labelKey}`)}
            </Menu.Item>
          ))}
        </Menu.Dropdown>
      </Menu>

      <ConfirmDialog
        open={pending !== null && pending.input === undefined}
        onOpenChange={(open) => !open && setPending(null)}
        title={pending ? t(`employees:${pending.labelKey}`) : undefined}
        description={t(`employees:bulk.confirm.${pending?.code ?? "default"}`, {
          count: selectedCount,
          defaultValue: t("employees:bulk.confirm.default", { count: selectedCount }),
        })}
        confirmLabel={t("employees:bulk.run")}
        confirmVariant="default"
        isLoading={mutation.isPending}
        onConfirm={() => pending && mutation.mutate({ action: pending, input: {} })}
      />

      <Modal
        opened={pending?.input === "manager"}
        onClose={() => {
          setPending(null);
          setManager(null);
        }}
        title={t("employees:bulk.actions.assignManager")}
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("employees:bulk.assignManager.description", { count: selectedCount })}
          </Text>
          <div>
            <Input.Label htmlFor="bulk-manager">{t("employees:fields.manager")}</Input.Label>
            <EmployeePicker id="bulk-manager" value={manager} onChange={setManager} />
          </div>
          <Group justify="flex-end">
            <Button type="button" variant="default" onClick={() => setPending(null)} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              disabled={!manager || mutation.isPending}
              onClick={() => pending && manager && mutation.mutate({ action: pending, input: { managerEmployeeId: manager.id } })}
            >
              {t("employees:bulk.continue")}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <BulkResultDialog
        open={result !== null}
        onOpenChange={(open) => !open && setResult(null)}
        title={result ? t(`employees:${result.action.labelKey}`) : ""}
        result={result?.value ?? null}
        nameById={resultNames}
      />
    </>
  );
}
