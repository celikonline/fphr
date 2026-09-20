import { useMemo, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Stamp, Upload } from "lucide-react";
import { Alert, Badge, Button, Card, Group, Modal, MultiSelect, Skeleton, Stack, Table, Text, TextInput, Title } from "@mantine/core";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { uploadDocumentFile } from "@/services/documents.service";
import { getEmployee } from "@/services/employees.service";
import {
  createVisaProcess,
  getVisaSettings,
  listAllVisaProcesses,
  listTemplateCandidates,
  setVisaProcessActive,
  updateVisaProcess,
  updateVisaSettings,
  type VisaProcess,
  type VisaSettings,
} from "@/services/visa.service";

const SETTINGS_KEY = ["visa", "settings"] as const;
const PROCESSES_KEY = ["visa", "processes", "manage"] as const;
const CANDIDATES_KEY = ["visa", "template-candidates"] as const;
const COUNTRY_CODE_PATTERN = /^[A-Za-z]{2,3}$/;

/** Ayarlar → Kurallar → Vize belge süreçleri: approver, template documents and process definitions (documents.visa.manage). */
export default function VisaProcessesPage() {
  const canManage = usePermission("documents.visa.manage");
  if (!canManage) return <NoAccess />;
  return <VisaProcessesAdmin />;
}

function VisaProcessesAdmin() {
  const { t } = useTranslation(["documents"]);
  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Stamp className="h-6 w-6" /> {t("documents:visa.settings.title")}
        </Title>
        <Text c="dimmed">{t("documents:visa.settings.subtitle")}</Text>
      </div>
      <SettingsCard />
      <ProcessesCard />
    </div>
  );
}

function SettingsCard() {
  const { t } = useTranslation(["documents"]);
  const settingsQuery = useQuery({ queryKey: SETTINGS_KEY, queryFn: getVisaSettings });

  return (
    <Card withBorder padding="lg">
      {settingsQuery.isLoading ? (
        <Skeleton h={160} />
      ) : settingsQuery.isError || !settingsQuery.data ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(settingsQuery.error, t("documents:visa.settings.loadFailed"))}
        </Text>
      ) : (
        <SettingsForm settings={settingsQuery.data} />
      )}
    </Card>
  );
}

function SettingsForm({ settings }: { settings: VisaSettings }) {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const fileInput = useRef<HTMLInputElement>(null);

  const candidatesQuery = useQuery({ queryKey: CANDIDATES_KEY, queryFn: () => listTemplateCandidates() });
  const savedApproverId = settings.approverEmployeeId ?? null;
  const savedApproverQuery = useQuery({
    queryKey: ["employees", "detail", savedApproverId],
    queryFn: () => getEmployee(savedApproverId as string),
    enabled: savedApproverId !== null,
  });

  // `undefined` = the admin has not touched the picker yet, so the saved approver (once its name is fetched) shows.
  const [pickedApprover, setPickedApprover] = useState<PickedEmployee | null | undefined>(undefined);
  const savedApprover: PickedEmployee | null = savedApproverQuery.data
    ? { id: savedApproverQuery.data.id, displayName: savedApproverQuery.data.displayName }
    : savedApproverId
      ? { id: savedApproverId, displayName: savedApproverId }
      : null;
  const approver = pickedApprover === undefined ? savedApprover : pickedApprover;
  const [templateIds, setTemplateIds] = useState<string[]>(settings.templateDocumentIds);
  const [error, setError] = useState<string | null>(null);

  const options = useMemo(() => {
    const known = (candidatesQuery.data ?? []).map((doc) => ({ value: doc.id, label: doc.title }));
    // A configured template that is no longer listed (deleted/pending) stays visible so it can be removed.
    const unknown = templateIds.filter((id) => !known.some((o) => o.value === id)).map((id) => ({ value: id, label: id }));
    return [...known, ...unknown];
  }, [candidatesQuery.data, templateIds]);

  const saveMutation = useMutation({
    mutationFn: () => updateVisaSettings({ approverEmployeeId: approver?.id ?? null, templateDocumentIds: templateIds }),
    onSuccess: (saved) => {
      setError(null);
      queryClient.setQueryData(SETTINGS_KEY, saved);
      toast({ variant: "success", title: t("common:success"), description: t("documents:visa.settings.saved") });
    },
    onError: (err) => setError(getApiErrorMessage(err, t("documents:visa.settings.genericError"))),
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadDocumentFile(file),
    onSuccess: (documentId) => {
      setTemplateIds((current) => (current.includes(documentId) ? current : [...current, documentId]));
      void queryClient.invalidateQueries({ queryKey: CANDIDATES_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("documents:visa.settings.templates.uploaded") });
    },
    onError: (err) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("documents:visa.settings.templates.uploadFailed")) }),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    saveMutation.mutate();
  }

  return (
    <form onSubmit={submit}>
      <Stack gap="lg">
        <Stack gap="xs">
          <Text fw={600}>{t("documents:visa.settings.approver.title")}</Text>
          <Text size="sm" c="dimmed">
            {t("documents:visa.settings.approver.description")}
          </Text>
          <EmployeePicker value={approver} onChange={setPickedApprover} allowClear placeholder={t("documents:visa.settings.approver.placeholder")} />
          {!approver && (
            <Alert color="yellow" variant="light" p="sm">
              {t("documents:visa.settings.approver.notConfigured")}
            </Alert>
          )}
        </Stack>

        <Stack gap="xs">
          <Text fw={600}>{t("documents:visa.settings.templates.title")}</Text>
          <Text size="sm" c="dimmed">
            {t("documents:visa.settings.templates.description")}
          </Text>
          <MultiSelect
            aria-label={t("documents:visa.settings.templates.title")}
            data={options}
            value={templateIds}
            onChange={setTemplateIds}
            searchable
            clearable
            placeholder={t("documents:visa.settings.templates.placeholder")}
            nothingFoundMessage={t("documents:visa.settings.templates.empty")}
          />
          <div>
            <input
              ref={fileInput}
              type="file"
              hidden
              onChange={(event) => {
                const file = event.currentTarget.files?.[0];
                event.currentTarget.value = "";
                if (file) uploadMutation.mutate(file);
              }}
            />
            <Button
              type="button"
              variant="light"
              size="xs"
              leftSection={<Upload size={14} />}
              loading={uploadMutation.isPending}
              onClick={() => fileInput.current?.click()}
            >
              {t("documents:visa.settings.templates.upload")}
            </Button>
          </div>
        </Stack>

        {error && (
          <Alert color="red" variant="light" p="sm" role="alert">
            {error}
          </Alert>
        )}
        <Group justify="flex-end">
          <Button type="submit" loading={saveMutation.isPending}>
            {t("documents:visa.settings.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function ProcessesCard() {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const [dialogTarget, setDialogTarget] = useState<VisaProcess | "new" | null>(null);
  const query = useQuery({ queryKey: PROCESSES_KEY, queryFn: listAllVisaProcesses });
  const rows = query.data ?? [];

  const toggleMutation = useMutation({
    mutationFn: (process: VisaProcess) => setVisaProcessActive(process.id, !process.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["visa", "processes"] });
      toast({ variant: "success", title: t("common:success"), description: t("documents:visa.settings.processes.statusSaved") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("documents:visa.settings.genericError")) }),
  });

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="md">
        <div>
          <Text fw={600}>{t("documents:visa.settings.processes.title")}</Text>
          <Text size="sm" c="dimmed">
            {t("documents:visa.settings.processes.subtitle")}
          </Text>
        </div>
        <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
          {t("documents:visa.settings.processes.add")}
        </Button>
      </Group>

      {query.isLoading ? (
        <Skeleton h={128} />
      ) : query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("documents:visa.settings.loadFailed"))}
        </Text>
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed" ta="center" p="lg">
          {t("documents:visa.settings.processes.empty")}
        </Text>
      ) : (
        <div className="overflow-x-auto">
          <Table verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("documents:visa.settings.processes.columns.name")}</Table.Th>
                <Table.Th>{t("documents:visa.settings.processes.columns.country")}</Table.Th>
                <Table.Th>{t("documents:visa.settings.processes.columns.status")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((process) => (
                <Table.Tr key={process.id}>
                  <Table.Td>{process.name}</Table.Td>
                  <Table.Td>{process.countryCode ?? "—"}</Table.Td>
                  <Table.Td>
                    <Badge variant={process.isActive ? "light" : "outline"} color={process.isActive ? "green" : "gray"}>
                      {process.isActive ? t("documents:visa.settings.processes.active") : t("documents:visa.settings.processes.inactive")}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end" wrap="nowrap">
                      <Button type="button" size="xs" variant="subtle" onClick={() => setDialogTarget(process)}>
                        {t("common:edit")}
                      </Button>
                      <Button
                        type="button"
                        size="xs"
                        variant="subtle"
                        color={process.isActive ? "red" : "teal"}
                        loading={toggleMutation.isPending && toggleMutation.variables?.id === process.id}
                        onClick={() => toggleMutation.mutate(process)}
                      >
                        {process.isActive ? t("documents:visa.settings.processes.deactivate") : t("documents:visa.settings.processes.reactivate")}
                      </Button>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </div>
      )}

      {dialogTarget && <ProcessDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </Card>
  );
}

function ProcessDialog({ target, onClose }: { target: VisaProcess | "new"; onClose: () => void }) {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;
  const [name, setName] = useState(editing?.name ?? "");
  const [countryCode, setCountryCode] = useState(editing?.countryCode ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), countryCode: countryCode.trim() || null };
      return editing ? updateVisaProcess(editing.id, input) : createVisaProcess(input);
    },
    onSuccess: () => {
      // Prefix invalidation: covers the admin list and the employee form's active list.
      void queryClient.invalidateQueries({ queryKey: ["visa", "processes"] });
      toast({ variant: "success", title: t("common:success"), description: t("documents:visa.settings.processes.saved") });
      onClose();
    },
    onError: (err) => {
      const code = getApiProblem(err)?.code;
      setError(
        code === "visa_document_process.already_exists"
          ? t("documents:visa.settings.processes.duplicate")
          : getApiErrorMessage(err, t("documents:visa.settings.genericError")),
      );
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("common:required"));
    if (countryCode.trim() && !COUNTRY_CODE_PATTERN.test(countryCode.trim())) return setError(t("documents:visa.settings.processes.countryInvalid"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={editing ? t("documents:visa.settings.processes.editTitle") : t("documents:visa.settings.processes.addTitle")}
      size="lg"
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="visa-process-name"
            label={t("documents:visa.settings.processes.name")}
            value={name}
            maxLength={255}
            withAsterisk
            disabled={mutation.isPending}
            onChange={(event) => setName(event.currentTarget.value)}
          />
          <TextInput
            id="visa-process-country"
            label={t("documents:visa.settings.processes.country")}
            description={t("documents:visa.settings.processes.countryHint")}
            value={countryCode}
            maxLength={3}
            disabled={mutation.isPending}
            onChange={(event) => setCountryCode(event.currentTarget.value.toUpperCase())}
          />
          {error && (
            <Alert color="red" variant="light" p="sm" role="alert">
              {error}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
