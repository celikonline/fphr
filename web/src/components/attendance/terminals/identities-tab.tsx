import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  NativeSelect,
  PasswordInput,
  Skeleton,
  Stack,
  Table,
  Text,
  Tooltip,
} from "@mantine/core";
import { FileSpreadsheet, Plus } from "lucide-react";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  addEmployeeCredential,
  listEmployeeCredentials,
  setCredentialActive,
  TERMINAL_CREDENTIAL_KINDS,
  type TerminalCredentialKind,
} from "@/services/attendance-terminals.service";

/**
 * Çalışan terminal kimlikleri (kart/PIN/dış kimlik). Girilen değer yalnız gönderilir ve hemen silinir;
 * liste yalnız tür + (sunucu verirse) son 2 hane maskeli + durum gösterir. Değer hiçbir yerde render edilmez.
 */
export default function TerminalIdentitiesTab({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation(["terminals"]);
  const queryClient = useQueryClient();
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [kind, setKind] = useState<TerminalCredentialKind>("card");
  const [value, setValue] = useState("");
  const listKey = ["attendance", "terminal-identities", employee?.id];

  const identities = useQuery({
    queryKey: listKey,
    queryFn: () => listEmployeeCredentials(employee!.id),
    enabled: employee !== null,
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: listKey });

  const add = useMutation({
    mutationFn: () => addEmployeeCredential({ employeeId: employee!.id, kind, value: value.trim() }),
    onSuccess: async () => {
      setValue("");
      toast({ variant: "success", description: t("terminals:credential.saved") });
      await refresh();
    },
    onError: (e) =>
      toast({ variant: "destructive", description: getApiErrorMessage(e, t("terminals:credential.saveFailed")) }),
  });
  const toggle = useMutation({
    mutationFn: (args: { id: string; active: boolean }) => setCredentialActive(args.id, args.active),
    onSuccess: refresh,
    onError: (e) =>
      toast({ variant: "destructive", description: getApiErrorMessage(e, t("terminals:credential.statusFailed")) }),
  });

  return (
    <Stack gap="md">
      <Group justify="space-between" align="flex-end">
        <div className="min-w-64">
          <EmployeePicker
            value={employee}
            onChange={setEmployee}
            label={t("terminals:credential.employee")}
            placeholder={t("terminals:credential.pickEmployee")}
          />
        </div>
        <Tooltip label={t("terminals:credential.importSoon")}>
          {/* T2 şablon kodu: terminal-credentials. T2 yazılana kadar pasif. */}
          <Button variant="default" leftSection={<FileSpreadsheet size={16} />} disabled data-disabled>
            {t("terminals:credential.import")}
          </Button>
        </Tooltip>
      </Group>

      {employee && canManage && (
        <Card withBorder padding="md">
          <Group align="flex-end">
            <NativeSelect
              label={t("terminals:credential.kind")}
              value={kind}
              onChange={(e) => setKind(e.currentTarget.value as TerminalCredentialKind)}
              data={TERMINAL_CREDENTIAL_KINDS.map((k) => ({ value: k, label: t(`terminals:credentialKind.${k}`) }))}
            />
            <PasswordInput
              label={t("terminals:credential.value")}
              description={t("terminals:credential.valueHint")}
              value={value}
              autoComplete="off"
              maxLength={200}
              onChange={(e) => setValue(e.currentTarget.value)}
            />
            <Button
              leftSection={<Plus size={16} />}
              disabled={!value.trim()}
              loading={add.isPending}
              onClick={() => add.mutate()}
            >
              {t("terminals:credential.add")}
            </Button>
          </Group>
        </Card>
      )}

      {employee && identities.isPending && <Skeleton h={80} />}
      {employee && identities.isError && <Alert color="red">{t("terminals:credential.loadFailed")}</Alert>}
      {employee &&
        identities.data &&
        (identities.data.length === 0 ? (
          <Text c="dimmed" size="sm">
            {t("terminals:credential.empty")}
          </Text>
        ) : (
          <Table verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("terminals:credential.kind")}</Table.Th>
                <Table.Th>{t("terminals:credential.masked")}</Table.Th>
                <Table.Th>{t("terminals:credential.status")}</Table.Th>
                {canManage && <Table.Th />}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {identities.data.map((c) => (
                <Table.Tr key={c.id}>
                  <Table.Td>{t(`terminals:credentialKind.${c.kind}`)}</Table.Td>
                  <Table.Td>{c.lastTwo ? `••${c.lastTwo}` : "••••"}</Table.Td>
                  <Table.Td>
                    <Badge color={c.isActive ? "green" : "gray"} variant="light">
                      {c.isActive ? t("terminals:credential.active") : t("terminals:credential.inactive")}
                    </Badge>
                  </Table.Td>
                  {canManage && (
                    <Table.Td>
                      <Button
                        size="compact-sm"
                        variant="default"
                        onClick={() => toggle.mutate({ id: c.id, active: !c.isActive })}
                      >
                        {c.isActive ? t("terminals:credential.deactivate") : t("terminals:credential.activate")}
                      </Button>
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ))}
    </Stack>
  );
}
