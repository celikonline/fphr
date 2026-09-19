import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ClipboardCheck, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Modal, Select, Skeleton, Stack, Text, Textarea, TextInput, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { listLocations } from "@/services/locations.service";
import { listRiskAssessments, recordRiskAssessment, type HazardClass, type RiskAssessment } from "@/services/ohs.service";

const ASSESSMENTS_QUERY_KEY = ["ohs", "risk-assessments"] as const;
const LOCATIONS_QUERY_KEY = ["organization", "locations", "for-ohs"] as const;

const HAZARD_CLASS_COLOR: Record<HazardClass, string> = { low: "green", medium: "yellow", high: "red" };

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function assessmentErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "ohs_risk_assessment.hazard_classification_missing") {
    return t("ohs:riskAssessments.errors.hazardClassificationMissing");
  }
  return getApiErrorMessage(error, t("ohs:riskAssessments.errors.generic"));
}

export default function OhsRiskAssessmentsPage() {
  const canView = usePermission("ohs.risk_assessments.view");
  const canManage = usePermission("ohs.risk_assessments.manage");
  if (!canView && !canManage) return <NoAccess />;
  return <OhsRiskAssessmentsAdmin />;
}

function OhsRiskAssessmentsAdmin() {
  const { t } = useTranslation(["ohs", "common"]);
  const canManage = usePermission("ohs.risk_assessments.manage");
  const isMobile = useMediaQuery("(max-width: 390px)");

  const locationsQuery = useQuery({
    queryKey: LOCATIONS_QUERY_KEY,
    queryFn: () => listLocations({ page: 1, pageSize: 200, sort: "name" }),
  });
  const locations = useMemo(() => locationsQuery.data?.items ?? [], [locationsQuery.data]);
  const locationName = (id: string) => locations.find((l) => l.id === id)?.name ?? id;

  const assessmentsQuery = useQuery({
    queryKey: ASSESSMENTS_QUERY_KEY,
    queryFn: () => listRiskAssessments(),
  });
  const assessments = useMemo(() => assessmentsQuery.data ?? [], [assessmentsQuery.data]);

  const [dialogOpen, setDialogOpen] = useState(false);

  const columns: ColumnDef<RiskAssessment>[] = [
    {
      id: "location",
      header: t("ohs:riskAssessments.columns.location"),
      meta: { label: t("ohs:riskAssessments.columns.location") },
      cell: ({ row }) => locationName(row.original.locationId),
    },
    {
      id: "hazardClass",
      header: t("ohs:riskAssessments.columns.hazardClass"),
      meta: { label: t("ohs:riskAssessments.columns.hazardClass") },
      cell: ({ row }) => (
        <Badge color={HAZARD_CLASS_COLOR[row.original.hazardClassAtAssessment]} variant="light">
          {t(`ohs:enum.hazardClass.${row.original.hazardClassAtAssessment}`)}
        </Badge>
      ),
    },
    {
      id: "assessmentDate",
      accessorKey: "assessmentDate",
      header: t("ohs:riskAssessments.columns.assessmentDate"),
      meta: { label: t("ohs:riskAssessments.columns.assessmentDate") },
    },
    {
      id: "nextDueDate",
      accessorKey: "nextDueDate",
      header: t("ohs:riskAssessments.columns.nextDueDate"),
      meta: { label: t("ohs:riskAssessments.columns.nextDueDate") },
    },
    {
      id: "preparedBy",
      accessorKey: "preparedBy",
      header: t("ohs:riskAssessments.columns.preparedBy"),
      meta: { label: t("ohs:riskAssessments.columns.preparedBy") },
    },
  ];

  const table = useDataTable({ columns, data: assessments, getRowId: (a) => a.id });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ClipboardCheck size={24} /> {t("ohs:riskAssessments.title")}
          </Title>
          <Text c="dimmed">{t("ohs:riskAssessments.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogOpen(true)}>
            {t("ohs:riskAssessments.add")}
          </Button>
        )}
      </Group>

      {assessmentsQuery.isError ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center" p="lg">
            {t("ohs:riskAssessments.errors.loadFailed")}
          </Text>
        </Card>
      ) : isMobile ? (
        <AssessmentsMobileList assessments={assessments} isLoading={assessmentsQuery.isLoading} locationName={locationName} />
      ) : (
        <Card withBorder padding={0}>
          <DataTable table={table} isLoading={assessmentsQuery.isLoading} emptyMessage={t("ohs:riskAssessments.empty")} />
        </Card>
      )}

      {dialogOpen && <AssessmentDialog locations={locations} onClose={() => setDialogOpen(false)} />}
    </div>
  );
}

function AssessmentsMobileList({
  assessments,
  isLoading,
  locationName,
}: {
  assessments: RiskAssessment[];
  isLoading: boolean;
  locationName: (id: string) => string;
}) {
  const { t } = useTranslation(["ohs", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (assessments.length === 0) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed" ta="center">
          {t("ohs:riskAssessments.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {assessments.map((assessment) => (
        <Card key={assessment.id} withBorder padding="md">
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              {locationName(assessment.locationId)}
            </Text>
            <Badge color={HAZARD_CLASS_COLOR[assessment.hazardClassAtAssessment]} variant="light" w="fit-content">
              {t(`ohs:enum.hazardClass.${assessment.hazardClassAtAssessment}`)}
            </Badge>
            <Text size="xs" c="dimmed">
              {assessment.assessmentDate} — {t("ohs:riskAssessments.columns.nextDueDate")}: {assessment.nextDueDate}
            </Text>
            <Text size="xs" c="dimmed">
              {assessment.preparedBy}
            </Text>
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}

function AssessmentDialog({ locations, onClose }: { locations: { id: string; name: string }[]; onClose: () => void }) {
  const { t } = useTranslation(["ohs", "common"]);
  const queryClient = useQueryClient();

  const [locationId, setLocationId] = useState<string | null>(null);
  const [assessmentDate, setAssessmentDate] = useState(today());
  const [preparedBy, setPreparedBy] = useState("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async () => {
      if (!locationId) return;
      await recordRiskAssessment(locationId, assessmentDate, preparedBy.trim(), note.trim() || null);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ASSESSMENTS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("ohs:riskAssessments.saved") });
      onClose();
    },
    onError: (err) => setError(assessmentErrorMessage(err, t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!locationId) return setError(t("ohs:riskAssessments.errors.locationRequired"));
    if (!preparedBy.trim()) return setError(t("ohs:riskAssessments.errors.preparedByRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("ohs:riskAssessments.add")} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Select
            label={t("ohs:riskAssessments.columns.location")}
            withAsterisk
            data={locations.map((l) => ({ value: l.id, label: l.name }))}
            value={locationId}
            onChange={setLocationId}
            disabled={mutation.isPending}
          />
          <TextInput
            type="date"
            label={t("ohs:riskAssessments.columns.assessmentDate")}
            withAsterisk
            value={assessmentDate}
            onChange={(event) => setAssessmentDate(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            label={t("ohs:riskAssessments.columns.preparedBy")}
            withAsterisk
            value={preparedBy}
            maxLength={255}
            onChange={(event) => setPreparedBy(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <Textarea
            label={t("ohs:hazardClassification.columns.note")}
            value={note}
            maxLength={1000}
            onChange={(event) => setNote(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
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
