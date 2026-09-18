import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { HardHat, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Modal, Select, Skeleton, Stack, Text, Textarea, TextInput, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listLocations, type Location } from "@/services/locations.service";
import {
  listCurrentHazardClassifications,
  setHazardClassification,
  type HazardClass,
  type HazardClassification,
} from "@/services/ohs.service";

const CLASSIFICATIONS_QUERY_KEY = ["ohs", "hazard-classifications", "current"] as const;
const LOCATIONS_QUERY_KEY = ["organization", "locations", "for-ohs"] as const;

const HAZARD_CLASS_COLOR: Record<HazardClass, string> = { low: "green", medium: "yellow", high: "red" };

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export default function OhsHazardClassificationPage() {
  const canView = usePermission("ohs.hazard_classifications.view");
  const canManage = usePermission("ohs.hazard_classifications.manage");
  if (!canView && !canManage) return <NoAccess />;
  return <OhsHazardClassificationAdmin />;
}

function OhsHazardClassificationAdmin() {
  const { t } = useTranslation(["ohs", "common"]);
  const canManage = usePermission("ohs.hazard_classifications.manage");
  const isMobile = useMediaQuery("(max-width: 390px)");

  const locationsQuery = useQuery({
    queryKey: LOCATIONS_QUERY_KEY,
    queryFn: () => listLocations({ page: 1, pageSize: 200, sort: "name" }),
  });
  const locations = useMemo(() => locationsQuery.data?.items ?? [], [locationsQuery.data]);
  const locationName = (id: string) => locations.find((l) => l.id === id)?.name ?? id;

  const classificationsQuery = useQuery({
    queryKey: CLASSIFICATIONS_QUERY_KEY,
    queryFn: () => listCurrentHazardClassifications(),
  });
  const classifications = useMemo(() => classificationsQuery.data ?? [], [classificationsQuery.data]);

  const [dialogTarget, setDialogTarget] = useState<HazardClassification | "new" | null>(null);

  const columns: ColumnDef<HazardClassification>[] = [
    {
      id: "location",
      header: t("ohs:hazardClassification.columns.location"),
      meta: { label: t("ohs:hazardClassification.columns.location") },
      cell: ({ row }) => locationName(row.original.locationId),
    },
    {
      id: "hazardClass",
      header: t("ohs:hazardClassification.columns.hazardClass"),
      meta: { label: t("ohs:hazardClassification.columns.hazardClass") },
      cell: ({ row }) => (
        <Badge color={HAZARD_CLASS_COLOR[row.original.hazardClass]} variant="light">
          {t(`ohs:enum.hazardClass.${row.original.hazardClass}`)}
        </Badge>
      ),
    },
    {
      id: "effectiveFrom",
      accessorKey: "effectiveFrom",
      header: t("ohs:hazardClassification.columns.effectiveFrom"),
      meta: { label: t("ohs:hazardClassification.columns.effectiveFrom") },
    },
    {
      id: "note",
      accessorKey: "note",
      header: t("ohs:hazardClassification.columns.note"),
      meta: { label: t("ohs:hazardClassification.columns.note") },
      cell: ({ row }) => row.original.note ?? "—",
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions"), headerClassName: "w-40" },
      enableSorting: false,
      cell: ({ row }) =>
        canManage && (
          <Group gap="xs" justify="flex-end" wrap="nowrap">
            <Button type="button" size="xs" variant="subtle" onClick={() => setDialogTarget(row.original)}>
              {t("ohs:hazardClassification.reclassify")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: classifications, getRowId: (c) => c.id });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <HardHat size={24} /> {t("ohs:hazardClassification.title")}
          </Title>
          <Text c="dimmed">{t("ohs:hazardClassification.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("ohs:hazardClassification.add")}
          </Button>
        )}
      </Group>

      {classificationsQuery.isError ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center" p="lg">
            {t("ohs:hazardClassification.errors.loadFailed")}
          </Text>
        </Card>
      ) : isMobile ? (
        <ClassificationsMobileList
          classifications={classifications}
          isLoading={classificationsQuery.isLoading}
          canManage={canManage}
          locationName={locationName}
          onEdit={setDialogTarget}
        />
      ) : (
        <Card withBorder padding={0}>
          <DataTable table={table} isLoading={classificationsQuery.isLoading} emptyMessage={t("ohs:hazardClassification.empty")} />
        </Card>
      )}

      {dialogTarget && (
        <ClassificationDialog target={dialogTarget} locations={locations} onClose={() => setDialogTarget(null)} />
      )}
    </div>
  );
}

function ClassificationsMobileList({
  classifications,
  isLoading,
  canManage,
  locationName,
  onEdit,
}: {
  classifications: HazardClassification[];
  isLoading: boolean;
  canManage: boolean;
  locationName: (id: string) => string;
  onEdit: (classification: HazardClassification) => void;
}) {
  const { t } = useTranslation(["ohs", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (classifications.length === 0) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed" ta="center">
          {t("ohs:hazardClassification.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {classifications.map((classification) => (
        <Card key={classification.id} withBorder padding="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Stack gap={4}>
              <Text size="sm" fw={500}>
                {locationName(classification.locationId)}
              </Text>
              <Badge color={HAZARD_CLASS_COLOR[classification.hazardClass]} variant="light" w="fit-content">
                {t(`ohs:enum.hazardClass.${classification.hazardClass}`)}
              </Badge>
              <Text size="xs" c="dimmed">
                {classification.effectiveFrom}
              </Text>
            </Stack>
            {canManage && (
              <Button type="button" size="xs" variant="subtle" onClick={() => onEdit(classification)}>
                {t("ohs:hazardClassification.reclassify")}
              </Button>
            )}
          </Group>
        </Card>
      ))}
    </Stack>
  );
}

function ClassificationDialog({
  target,
  locations,
  onClose,
}: {
  target: HazardClassification | "new";
  locations: Location[];
  onClose: () => void;
}) {
  const { t } = useTranslation(["ohs", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;

  const [locationId, setLocationId] = useState<string | null>(editing?.locationId ?? null);
  const [hazardClass, setHazardClass] = useState<HazardClass | null>(editing?.hazardClass ?? null);
  const [effectiveFrom, setEffectiveFrom] = useState(today());
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async () => {
      if (!locationId || !hazardClass) return;
      await setHazardClassification(locationId, hazardClass, effectiveFrom, note.trim() || null);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CLASSIFICATIONS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("ohs:hazardClassification.saved") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("ohs:hazardClassification.errors.generic"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!locationId) return setError(t("ohs:hazardClassification.errors.locationRequired"));
    if (!hazardClass) return setError(t("ohs:hazardClassification.errors.hazardClassRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={editing ? t("ohs:hazardClassification.reclassify") : t("ohs:hazardClassification.add")}
      size="32rem"
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <Select
            label={t("ohs:hazardClassification.columns.location")}
            withAsterisk
            data={locations.map((l) => ({ value: l.id, label: l.name }))}
            value={locationId}
            onChange={setLocationId}
            disabled={Boolean(editing) || mutation.isPending}
          />
          <Select
            label={t("ohs:hazardClassification.columns.hazardClass")}
            withAsterisk
            data={[
              { value: "low", label: t("ohs:enum.hazardClass.low") },
              { value: "medium", label: t("ohs:enum.hazardClass.medium") },
              { value: "high", label: t("ohs:enum.hazardClass.high") },
            ]}
            value={hazardClass}
            onChange={(value) => setHazardClass(value as HazardClass | null)}
            disabled={mutation.isPending}
          />
          <TextInput
            type="date"
            label={t("ohs:hazardClassification.columns.effectiveFrom")}
            withAsterisk
            value={effectiveFrom}
            onChange={(event) => setEffectiveFrom(event.currentTarget.value)}
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
