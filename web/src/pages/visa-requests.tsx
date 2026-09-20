import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Plus, Stamp } from "lucide-react";
import { Alert, Badge, Button, Card, Group, Input, Modal, Select, Skeleton, Stack, Table, Text, Textarea, TextInput, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  VISA_TYPES,
  listMyVisaRequests,
  listVisaProcesses,
  listVisaRequestDocuments,
  submitVisaRequest,
  type VisaProcess,
  type VisaRequest,
  type VisaRequestStatus,
  type VisaType,
} from "@/services/visa.service";

const MY_VISA_REQUESTS_KEY = ["visa", "requests", "me"] as const;
const VISA_PROCESSES_KEY = ["visa", "processes"] as const;
const COUNTRY_CODE_PATTERN = /^[A-Za-z]{2,3}$/;

const STATUS_COLOR: Record<VisaRequestStatus, string> = {
  pending: "gray",
  awaitingApproval: "yellow",
  approved: "blue",
  rejected: "red",
  completed: "green",
};

/** "+ Talep" → Vize belgesi talebi: the caller's own visa document requests (documents.my.visa_requests). */
export default function VisaRequestsPage() {
  const canUse = usePermission("documents.my.visa_requests");
  if (!canUse) return <NoAccess />;
  return <VisaRequests />;
}

function VisaRequests() {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  // Tek "Talep" hub'ından (/app/requests) `?new=1` ile gelince form ilk render'da açık başlar (roadmap #42).
  const [searchParams, setSearchParams] = useSearchParams();
  const [adding, setAdding] = useState(() => searchParams.get("new") === "1");
  const [viewing, setViewing] = useState<VisaRequest | null>(null);
  const query = useQuery({ queryKey: MY_VISA_REQUESTS_KEY, queryFn: () => listMyVisaRequests() });
  const processesQuery = useQuery({ queryKey: VISA_PROCESSES_KEY, queryFn: listVisaProcesses });
  const processes = processesQuery.data ?? [];

  useEffect(() => {
    if (searchParams.get("new") === "1") {
      setSearchParams(
        (params) => {
          params.delete("new");
          return params;
        },
        { replace: true },
      );
    }
  }, [searchParams, setSearchParams]);

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Stamp className="h-6 w-6" /> {t("documents:visa.my.title")}
          </Title>
          <Text c="dimmed">{t("documents:visa.my.subtitle")}</Text>
        </div>
        <Button type="button" leftSection={<Plus size={16} />} onClick={() => setAdding(true)}>
          {t("documents:visa.my.newRequest")}
        </Button>
      </Group>

      <Card withBorder padding="lg">
        <div className="overflow-x-auto">
          {query.isLoading ? (
            <Skeleton h={128} />
          ) : query.isError ? (
            <Text size="sm" c="red">
              {getApiErrorMessage(query.error, t("documents:visa.my.loadFailed"))}
            </Text>
          ) : (query.data ?? []).length === 0 ? (
            <Text size="sm" c="dimmed" ta="center" p="lg">
              {t("documents:visa.my.empty")}
            </Text>
          ) : (
            <RequestsTable rows={query.data ?? []} processes={processes} onView={setViewing} />
          )}
        </div>
      </Card>

      {adding && (
        <VisaRequestDialog
          processes={processes}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false);
            toast({ variant: "success", title: t("common:success"), description: t("documents:visa.my.submitted") });
            void queryClient.invalidateQueries({ queryKey: MY_VISA_REQUESTS_KEY });
          }}
        />
      )}

      {viewing && <DeliveredDocumentsDialog request={viewing} onClose={() => setViewing(null)} />}
    </div>
  );
}

function RequestsTable({ rows, processes, onView }: { rows: VisaRequest[]; processes: VisaProcess[]; onView: (row: VisaRequest) => void }) {
  const { t, i18n } = useTranslation(["documents", "common"]);
  const processName = (id: string | null | undefined) => processes.find((p) => p.id === id)?.name ?? "—";

  return (
    <Table verticalSpacing="sm" highlightOnHover>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>{t("documents:visa.my.columns.country")}</Table.Th>
          <Table.Th>{t("documents:visa.my.columns.visaType")}</Table.Th>
          <Table.Th>{t("documents:visa.my.columns.travel")}</Table.Th>
          <Table.Th>{t("documents:visa.my.columns.process")}</Table.Th>
          <Table.Th>{t("documents:visa.my.columns.status")}</Table.Th>
          <Table.Th>{t("documents:visa.my.columns.createdAt")}</Table.Th>
          <Table.Th />
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {rows.map((row) => (
          <Table.Tr key={row.id}>
            <Table.Td>
              <Text size="sm" fw={500}>
                {row.countryCode}
              </Text>
            </Table.Td>
            <Table.Td>{t(`documents:visa.type.${row.visaType}`, { defaultValue: row.visaType })}</Table.Td>
            <Table.Td>
              {formatDateOnly(row.travelStart.slice(0, 10), i18n.language)} – {formatDateOnly(row.travelEnd.slice(0, 10), i18n.language)}
            </Table.Td>
            <Table.Td>{processName(row.visaDocumentProcessId)}</Table.Td>
            <Table.Td>
              <Stack gap={2} align="flex-start">
                <Badge variant="light" color={STATUS_COLOR[row.status]}>
                  {t(`documents:visa.status.${row.status}`)}
                </Badge>
                {row.status === "awaitingApproval" && (
                  <Text size="xs" c="dimmed">
                    {t("documents:visa.my.awaitingHint")}
                  </Text>
                )}
                {row.status === "rejected" && (
                  <Text size="xs" c="red">
                    {row.rejectionReason ? t("documents:visa.my.rejectedReason", { reason: row.rejectionReason }) : t("documents:visa.my.rejectedNoReason")}
                  </Text>
                )}
              </Stack>
            </Table.Td>
            <Table.Td>{formatDateOnly(row.createdAt.slice(0, 10), i18n.language)}</Table.Td>
            <Table.Td>
              {row.status === "completed" && (
                <Button type="button" size="xs" variant="subtle" onClick={() => onView(row)}>
                  {t("documents:visa.my.documents.show")}
                </Button>
              )}
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

function VisaRequestDialog({ processes, onClose, onSaved }: { processes: VisaProcess[]; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation(["documents", "common"]);
  const [processId, setProcessId] = useState<string | null>(null);
  const [countryCode, setCountryCode] = useState("");
  const [visaType, setVisaType] = useState<VisaType | null>(null);
  const [travelStart, setTravelStart] = useState("");
  const [travelEnd, setTravelEnd] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: submitVisaRequest,
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("documents:visa.form.genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!travelStart || !travelEnd) return setError(t("documents:visa.form.validation.datesRequired"));
    if (travelEnd < travelStart) return setError(t("documents:visa.form.validation.datesOrder"));
    if (!COUNTRY_CODE_PATTERN.test(countryCode.trim())) return setError(t("documents:visa.form.validation.countryFormat"));
    if (!visaType) return setError(t("documents:visa.form.validation.visaTypeRequired"));
    setError(null);
    mutation.mutate({
      visaDocumentProcessId: processId,
      travelStart,
      travelEnd,
      countryCode: countryCode.trim().toUpperCase(),
      visaType,
      description: description.trim() || null,
    });
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={t("documents:visa.form.title")} size="lg">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("documents:visa.form.description")}
          </Text>
          {processes.length > 0 && (
            <Select
              id="visa-process"
              label={t("documents:visa.form.process")}
              placeholder={t("documents:visa.form.processPlaceholder")}
              data={processes.map((p) => ({ value: p.id, label: p.name }))}
              value={processId}
              clearable
              disabled={busy}
              onChange={(value) => {
                setProcessId(value);
                // Convenience only: a process tied to one country pre-fills the (still editable) country code.
                const country = processes.find((p) => p.id === value)?.countryCode;
                if (country && !countryCode) setCountryCode(country);
              }}
            />
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput
              id="visa-country"
              label={t("documents:visa.form.country")}
              description={t("documents:visa.form.countryHint")}
              value={countryCode}
              maxLength={3}
              withAsterisk
              disabled={busy}
              onChange={(event) => setCountryCode(event.currentTarget.value.toUpperCase())}
            />
            <Select
              id="visa-type"
              label={t("documents:visa.form.visaType")}
              placeholder={t("documents:visa.form.visaTypePlaceholder")}
              data={VISA_TYPES.map((type) => ({ value: type, label: t(`documents:visa.type.${type}`) }))}
              value={visaType}
              withAsterisk
              disabled={busy}
              onChange={(value) => setVisaType(value as VisaType | null)}
            />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Input.Wrapper id="visa-start" label={t("documents:visa.form.travelStart")} withAsterisk>
              <DatePicker id="visa-start" value={travelStart} disabled={busy} onChange={setTravelStart} />
            </Input.Wrapper>
            <Input.Wrapper id="visa-end" label={t("documents:visa.form.travelEnd")} withAsterisk>
              <DatePicker id="visa-end" value={travelEnd} min={travelStart || undefined} disabled={busy} onChange={setTravelEnd} />
            </Input.Wrapper>
          </div>
          <Textarea
            id="visa-notes"
            label={t("documents:visa.form.notes")}
            placeholder={t("documents:visa.form.notesPlaceholder")}
            value={description}
            maxLength={2000}
            rows={3}
            disabled={busy}
            onChange={(event) => setDescription(event.currentTarget.value)}
          />
          {error && (
            <Alert color="red" variant="light" p="sm" role="alert">
              {error}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={busy}>
              {t("documents:visa.form.submit")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function DeliveredDocumentsDialog({ request, onClose }: { request: VisaRequest; onClose: () => void }) {
  const { t } = useTranslation(["documents", "common"]);
  // Presigned URLs expire after 15 minutes: never cache this list across dialog openings.
  const query = useQuery({
    queryKey: ["visa", "requests", request.id, "documents"],
    queryFn: () => listVisaRequestDocuments(request.id),
    gcTime: 0,
  });

  return (
    <Modal opened onClose={onClose} title={t("documents:visa.my.documents.title")} size="lg">
      <Stack gap="sm">
        {query.isLoading ? (
          <Skeleton h={64} />
        ) : query.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(query.error, t("documents:visa.my.documents.loadFailed"))}
          </Text>
        ) : (query.data ?? []).length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("documents:visa.my.documents.empty")}
          </Text>
        ) : (
          (query.data ?? []).map((doc) => (
            <Group key={doc.id} justify="space-between" wrap="nowrap">
              <Text size="sm" truncate="end">
                {doc.title}
              </Text>
              <Button component="a" href={doc.downloadUrl} target="_blank" rel="noopener noreferrer" size="xs" variant="light" leftSection={<Download size={14} />}>
                {t("documents:visa.my.documents.download")}
              </Button>
            </Group>
          ))
        )}
        <Group justify="flex-end">
          <Button type="button" variant="default" onClick={onClose}>
            {t("common:close")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
