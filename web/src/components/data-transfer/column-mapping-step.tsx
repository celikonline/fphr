import { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  List,
  Loader,
  Radio,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  Title,
} from "@mantine/core";
import { AlertTriangle, CheckCircle2, KeyRound } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus, getApiProblem, type ApiProblem } from "@/lib/api-error";
import {
  DataTransferPermissions,
  isBlockingColumn,
  runImportJob,
  saveImportJobMapping,
  type ImportFailurePolicy,
  type ImportJob,
  type ImportMode,
  type ImportTemplate,
} from "@/services/data-transfer.service";
import WizardSteps from "./wizard-steps";

const IGNORE = "__ignore__";
const UNMAPPED_REQUIRED = "IMPORT_UNMAPPED_REQUIRED";

type MappingState = Record<number, string | null>;
type Phase = "idle" | "saving" | "running";

interface ColumnMappingStepProps {
  job: ImportJob;
  template: ImportTemplate;
}

function buildInitialMapping(job: ImportJob, template: ImportTemplate): MappingState {
  const validKeys = new Set(template.columns.map((column) => column.key));
  const saved = new Map(job.mapping.map((m) => [m.sourceIndex, m.targetKey]));
  const useSaved = job.status === "Mapped" && job.mapping.length > 0;
  const used = new Set<string>();
  const result: MappingState = {};
  for (const source of job.sourceColumns) {
    let target = useSaved ? (saved.get(source.index) ?? null) : source.suggestedTargetKey;
    if (target && (!validKeys.has(target) || used.has(target))) target = null;
    if (target) used.add(target);
    result[source.index] = target;
  }
  return result;
}

/** Flattens ProblemDetails `args` (shape not fixed by the contract) into a list of strings. */
function extractArgValues(args: unknown): string[] {
  if (!args || typeof args !== "object") return [];
  const values: string[] = [];
  const visit = (value: unknown) => {
    if (Array.isArray(value)) value.forEach(visit);
    else if (typeof value === "string") value.split(",").forEach((part) => part.trim() && values.push(part.trim()));
  };
  Object.values(args as Record<string, unknown>).forEach(visit);
  return values;
}

export default function ColumnMappingStep({ job, template }: ColumnMappingStepProps) {
  const { t } = useTranslation(["dataTransfer", "common"]);
  const queryClient = useQueryClient();
  const canRun = usePermission(DataTransferPermissions.Run);

  const [mapping, setMapping] = useState<MappingState>(() => buildInitialMapping(job, template));
  const supportsUpdateOnly = template.supportedModes.includes("UpdateOnly");
  const [mode, setMode] = useState<ImportMode>(job.mode === "UpdateOnly" && supportsUpdateOnly ? "UpdateOnly" : "Upsert");
  const [failurePolicy, setFailurePolicy] = useState<ImportFailurePolicy>(job.failurePolicy ?? "SkipInvalidRows");
  const [phase, setPhaseState] = useState<Phase>("idle");
  const phaseRef = useRef<Phase>("idle");
  const setPhase = (next: Phase) => {
    phaseRef.current = next;
    setPhaseState(next);
  };
  const [serverMissing, setServerMissing] = useState<string[] | null>(null);

  const columnsByKey = useMemo(() => new Map(template.columns.map((column) => [column.key, column])), [template]);
  const usedTargets = useMemo(
    () => new Map(Object.entries(mapping).filter(([, key]) => key).map(([index, key]) => [key as string, Number(index)])),
    [mapping]
  );

  const missingBlocking = template.columns.filter((column) => isBlockingColumn(column) && !usedTargets.has(column.key));
  const missingOnInsert =
    mode === "Upsert"
      ? template.columns.filter((column) => column.requirement === "OnInsert" && !usedTargets.has(column.key))
      : [];
  const mappedCount = usedTargets.size;

  const headerFor = (key: string) => columnsByKey.get(key)?.header ?? key;

  function setTarget(sourceIndex: number, value: string) {
    setServerMissing(null);
    setMapping((prev) => ({ ...prev, [sourceIndex]: value === IGNORE ? null : value }));
  }

  const invalidateLists = () => queryClient.invalidateQueries({ queryKey: ["datatransfer", "import-jobs"] });

  const runMutation = useMutation({
    mutationFn: async () => {
      setPhase("saving");
      const saved = await saveImportJobMapping(job.id, {
        mapping: job.sourceColumns.map((source) => ({ sourceIndex: source.index, targetKey: mapping[source.index] ?? null })),
        mode,
        failurePolicy,
      });
      queryClient.setQueryData(["datatransfer", "import-job", job.id], saved);
      setPhase("running");
      return runImportJob(job.id);
    },
    onSuccess: (finished) => {
      setPhase("idle");
      queryClient.setQueryData(["datatransfer", "import-job", job.id], finished);
      invalidateLists();
    },
    onError: (error) => {
      const wasRunning = phaseRef.current === "running";
      setPhase("idle");
      const problem = getApiProblem(error);
      if (getApiErrorStatus(error) === 422 && problem?.code === UNMAPPED_REQUIRED) {
        const values = extractArgValues((problem as ApiProblem & { args?: unknown }).args);
        setServerMissing(values.length > 0 ? values : missingBlocking.map((column) => column.key));
        return;
      }
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t(wasRunning ? "dataTransfer:run.error" : "dataTransfer:mapping.saveError")),
      });
      invalidateLists();
      queryClient.invalidateQueries({ queryKey: ["datatransfer", "import-job", job.id] });
    },
  });

  if (phase === "running") {
    return (
      <div className="space-y-6">
        <WizardSteps active={["run"]} />
        <Card withBorder padding="xl">
          <Stack align="center" gap="sm" py="xl" ta="center">
            <Loader size="lg" />
            <Text size="lg" fw={500}>
              {t("dataTransfer:run.running")}
            </Text>
            <Text size="sm" c="dimmed">
              {t("dataTransfer:run.runningHint")}
            </Text>
          </Stack>
        </Card>
      </div>
    );
  }

  const isBusy = runMutation.isPending;

  return (
    <div className="space-y-6">
      <WizardSteps active={["mapping", "options"]} />

      <Card withBorder padding={0}>
        <Stack gap={4} p="lg">
          <Title order={4}>{t("dataTransfer:mapping.title")}</Title>
          <Text size="sm" c="dimmed">
            {t("dataTransfer:mapping.subtitle", { mapped: mappedCount, total: job.sourceColumns.length })}
          </Text>
        </Stack>
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="sm" horizontalSpacing="lg">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("dataTransfer:mapping.sourceColumn")}</Table.Th>
                <Table.Th>{t("dataTransfer:mapping.samples")}</Table.Th>
                <Table.Th w={288}>{t("dataTransfer:mapping.target")}</Table.Th>
                <Table.Th w={112} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {job.sourceColumns.map((source) => {
                const target = mapping[source.index] ?? null;
                const autoMatched = source.autoMatched && target !== null && target === source.suggestedTargetKey;
                return (
                  <Table.Tr key={source.index}>
                    <Table.Td fw={500}>
                      {source.header || t("dataTransfer:mapping.untitledColumn", { index: source.index + 1 })}
                    </Table.Td>
                    <Table.Td maw={288}>
                      {source.samples.length === 0 ? (
                        <Text span c="dimmed" inherit>
                          —
                        </Text>
                      ) : (
                        <Stack gap={0}>
                          {source.samples.slice(0, 3).map((sample, i) => (
                            <Text key={i} size="xs" c="dimmed" truncate title={sample}>
                              {sample || "—"}
                            </Text>
                          ))}
                        </Stack>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Select
                        aria-label={t("dataTransfer:mapping.target")}
                        value={target ?? IGNORE}
                        onChange={(value) => setTarget(source.index, value ?? IGNORE)}
                        allowDeselect={false}
                        disabled={isBusy || !canRun}
                        styles={target === null ? { input: { color: "var(--mantine-color-dimmed)" } } : undefined}
                        data={[
                          { value: IGNORE, label: t("dataTransfer:mapping.ignore") },
                          ...template.columns.map((column) => {
                            const takenBy = usedTargets.get(column.key);
                            const taken = takenBy !== undefined && takenBy !== source.index;
                            const required = isBlockingColumn(column) ? " *" : "";
                            const takenLabel = taken ? ` (${t("dataTransfer:mapping.alreadyMapped")})` : "";
                            return { value: column.key, label: `${column.header}${required}${takenLabel}`, disabled: taken };
                          }),
                        ]}
                      />
                    </Table.Td>
                    <Table.Td>
                      {autoMatched ? (
                        <Badge variant="light" color="green" leftSection={<CheckCircle2 size={12} />}>
                          {t("dataTransfer:mapping.autoMatched")}
                        </Badge>
                      ) : target === null ? (
                        <Badge variant="outline" color="gray" fw={400}>
                          {t("dataTransfer:mapping.ignored")}
                        </Badge>
                      ) : null}
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>

        <Stack gap="sm" p="lg">
          {missingBlocking.length > 0 ? (
            <Alert
              variant="light"
              color="yellow"
              icon={<AlertTriangle size={16} />}
              title={t("dataTransfer:mapping.missingRequiredTitle")}
            >
              <Group gap={6} mt={4}>
                {missingBlocking.map((column) => (
                  <Badge
                    key={column.key}
                    variant="outline"
                    color="gray"
                    tt="none"
                    leftSection={column.isMatchKey ? <KeyRound size={12} /> : undefined}
                  >
                    {column.header}
                  </Badge>
                ))}
              </Group>
            </Alert>
          ) : (
            <Alert variant="light" color="green" icon={<CheckCircle2 size={16} />}>
              {t("dataTransfer:mapping.allRequiredMapped")}
            </Alert>
          )}
          {missingOnInsert.length > 0 && (
            <Text size="sm" c="dimmed">
              {t("dataTransfer:mapping.onInsertHint", { columns: missingOnInsert.map((c) => c.header).join(", ") })}
            </Text>
          )}
          {serverMissing && (
            <Alert
              variant="light"
              color="red"
              icon={<AlertTriangle size={16} />}
              title={t("dataTransfer:errorCodes.IMPORT_UNMAPPED_REQUIRED")}
            >
              {serverMissing.map(headerFor).join(", ")}
            </Alert>
          )}
        </Stack>
      </Card>

      <Card withBorder padding="lg">
        <Title order={4} mb="lg">
          {t("dataTransfer:options.title")}
        </Title>
        <Stack gap="xl">
          <Group align="flex-start" gap="sm" wrap="nowrap">
            <Switch
              id="dt-update-only"
              checked={mode === "UpdateOnly"}
              onChange={(event) => setMode(event.currentTarget.checked ? "UpdateOnly" : "Upsert")}
              disabled={isBusy || !canRun || !supportsUpdateOnly}
            />
            <Stack gap={4}>
              <Text component="label" htmlFor="dt-update-only" size="sm" fw={500}>
                {t("dataTransfer:options.updateOnly")}
              </Text>
              <List size="sm" c="dimmed" spacing={2}>
                <List.Item>{t("dataTransfer:options.updateOnlyHelp.updates")}</List.Item>
                <List.Item>{t("dataTransfer:options.updateOnlyHelp.noInsert")}</List.Item>
                <List.Item>{t("dataTransfer:options.updateOnlyHelp.noMatchError")}</List.Item>
                <List.Item>{t("dataTransfer:options.updateOnlyHelp.mappedOnly")}</List.Item>
              </List>
              {!supportsUpdateOnly && (
                <Text size="xs" c="dimmed">
                  {t("dataTransfer:options.updateOnlyUnsupported")}
                </Text>
              )}
            </Stack>
          </Group>

          <Radio.Group
            label={t("dataTransfer:options.failurePolicy")}
            value={failurePolicy}
            onChange={(value) => setFailurePolicy(value as ImportFailurePolicy)}
            disabled={isBusy || !canRun}
          >
            <Stack gap="sm" mt="xs">
              {(["SkipInvalidRows", "AllOrNothing"] as const).map((policy) => (
                <Radio
                  key={policy}
                  id={`dt-policy-${policy}`}
                  value={policy}
                  label={t(`dataTransfer:failurePolicy.${policy}`)}
                  description={t(`dataTransfer:failurePolicy.${policy}Help`)}
                />
              ))}
            </Stack>
          </Radio.Group>
        </Stack>
      </Card>

      <Group justify="flex-end" gap="sm">
        {!canRun && (
          <Text size="sm" c="dimmed">
            {t("dataTransfer:run.noPermission")}
          </Text>
        )}
        <Button
          type="button"
          onClick={() => runMutation.mutate()}
          disabled={isBusy || !canRun || missingBlocking.length > 0}
          leftSection={isBusy ? <Loader size={14} /> : undefined}
        >
          {phase === "saving" ? t("common:saving") : t("dataTransfer:mapping.saveAndContinue")}
        </Button>
      </Group>
    </div>
  );
}
