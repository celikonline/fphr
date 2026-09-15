import { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, KeyRound, Loader2 } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus, getApiProblem, type ApiProblem } from "@/lib/api-error";
import { cn } from "@/lib/utils";
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
        <Card>
          <CardContent className="flex flex-col items-center gap-3 p-12 text-center">
            <Loader2 className="h-10 w-10 animate-spin text-primary" />
            <p className="text-lg font-medium">{t("dataTransfer:run.running")}</p>
            <p className="text-sm text-muted-foreground">{t("dataTransfer:run.runningHint")}</p>
          </CardContent>
        </Card>
      </div>
    );
  }

  const isBusy = runMutation.isPending;

  return (
    <div className="space-y-6">
      <WizardSteps active={["mapping", "options"]} />

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t("dataTransfer:mapping.title")}</CardTitle>
          <CardDescription>
            {t("dataTransfer:mapping.subtitle", { mapped: mappedCount, total: job.sourceColumns.length })}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 p-0 pb-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="pl-6">{t("dataTransfer:mapping.sourceColumn")}</TableHead>
                <TableHead>{t("dataTransfer:mapping.samples")}</TableHead>
                <TableHead className="w-72">{t("dataTransfer:mapping.target")}</TableHead>
                <TableHead className="w-28 pr-6" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {job.sourceColumns.map((source) => {
                const target = mapping[source.index] ?? null;
                const autoMatched = source.autoMatched && target !== null && target === source.suggestedTargetKey;
                return (
                  <TableRow key={source.index}>
                    <TableCell className="pl-6 font-medium">
                      {source.header || t("dataTransfer:mapping.untitledColumn", { index: source.index + 1 })}
                    </TableCell>
                    <TableCell className="max-w-[18rem]">
                      {source.samples.length === 0 ? (
                        <span className="text-muted-foreground">—</span>
                      ) : (
                        <div className="flex flex-col text-xs text-muted-foreground">
                          {source.samples.slice(0, 3).map((sample, i) => (
                            <span key={i} className="truncate" title={sample}>
                              {sample || "—"}
                            </span>
                          ))}
                        </div>
                      )}
                    </TableCell>
                    <TableCell>
                      <Select
                        value={target ?? IGNORE}
                        onValueChange={(value) => setTarget(source.index, value)}
                        disabled={isBusy || !canRun}
                      >
                        <SelectTrigger
                          aria-label={t("dataTransfer:mapping.target")}
                          className={cn(target === null && "text-muted-foreground")}
                        >
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value={IGNORE}>{t("dataTransfer:mapping.ignore")}</SelectItem>
                          {template.columns.map((column) => {
                            const takenBy = usedTargets.get(column.key);
                            const taken = takenBy !== undefined && takenBy !== source.index;
                            return (
                              <SelectItem key={column.key} value={column.key} disabled={taken}>
                                {column.header}
                                {isBlockingColumn(column) ? " *" : ""}
                                {taken ? ` (${t("dataTransfer:mapping.alreadyMapped")})` : ""}
                              </SelectItem>
                            );
                          })}
                        </SelectContent>
                      </Select>
                    </TableCell>
                    <TableCell className="pr-6">
                      {autoMatched ? (
                        <Badge variant="success" className="gap-1 whitespace-nowrap">
                          <CheckCircle2 className="h-3 w-3" /> {t("dataTransfer:mapping.autoMatched")}
                        </Badge>
                      ) : target === null ? (
                        <Badge variant="outline" className="whitespace-nowrap font-normal text-muted-foreground">
                          {t("dataTransfer:mapping.ignored")}
                        </Badge>
                      ) : null}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>

          <div className="space-y-3 px-6">
            {missingBlocking.length > 0 ? (
              <Alert variant="warning">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("dataTransfer:mapping.missingRequiredTitle")}</AlertTitle>
                <AlertDescription>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {missingBlocking.map((column) => (
                      <Badge key={column.key} variant="outline" className="gap-1">
                        {column.isMatchKey && <KeyRound className="h-3 w-3" />}
                        {column.header}
                      </Badge>
                    ))}
                  </div>
                </AlertDescription>
              </Alert>
            ) : (
              <Alert variant="success">
                <CheckCircle2 className="h-4 w-4" />
                <AlertDescription>{t("dataTransfer:mapping.allRequiredMapped")}</AlertDescription>
              </Alert>
            )}
            {missingOnInsert.length > 0 && (
              <p className="text-sm text-muted-foreground">
                {t("dataTransfer:mapping.onInsertHint", { columns: missingOnInsert.map((c) => c.header).join(", ") })}
              </p>
            )}
            {serverMissing && (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("dataTransfer:errorCodes.IMPORT_UNMAPPED_REQUIRED")}</AlertTitle>
                <AlertDescription>{serverMissing.map(headerFor).join(", ")}</AlertDescription>
              </Alert>
            )}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t("dataTransfer:options.title")}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-6">
          <div className="flex items-start gap-3">
            <Switch
              id="dt-update-only"
              checked={mode === "UpdateOnly"}
              onCheckedChange={(checked) => setMode(checked ? "UpdateOnly" : "Upsert")}
              disabled={isBusy || !canRun || !supportsUpdateOnly}
            />
            <div className="space-y-1">
              <Label htmlFor="dt-update-only" className="font-medium">
                {t("dataTransfer:options.updateOnly")}
              </Label>
              <ul className="list-disc space-y-0.5 pl-4 text-sm text-muted-foreground">
                <li>{t("dataTransfer:options.updateOnlyHelp.updates")}</li>
                <li>{t("dataTransfer:options.updateOnlyHelp.noInsert")}</li>
                <li>{t("dataTransfer:options.updateOnlyHelp.noMatchError")}</li>
                <li>{t("dataTransfer:options.updateOnlyHelp.mappedOnly")}</li>
              </ul>
              {!supportsUpdateOnly && (
                <p className="text-xs text-muted-foreground">{t("dataTransfer:options.updateOnlyUnsupported")}</p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <p className="text-sm font-medium">{t("dataTransfer:options.failurePolicy")}</p>
            <RadioGroup
              value={failurePolicy}
              onValueChange={(value) => setFailurePolicy(value as ImportFailurePolicy)}
              disabled={isBusy || !canRun}
            >
              {(["SkipInvalidRows", "AllOrNothing"] as const).map((policy) => (
                <div key={policy} className="flex items-start gap-2">
                  <RadioGroupItem id={`dt-policy-${policy}`} value={policy} className="mt-0.5" />
                  <div>
                    <Label htmlFor={`dt-policy-${policy}`}>{t(`dataTransfer:failurePolicy.${policy}`)}</Label>
                    <p className="text-xs text-muted-foreground">{t(`dataTransfer:failurePolicy.${policy}Help`)}</p>
                  </div>
                </div>
              ))}
            </RadioGroup>
          </div>
        </CardContent>
      </Card>

      <div className="flex flex-wrap items-center justify-end gap-3">
        {!canRun && <p className="text-sm text-muted-foreground">{t("dataTransfer:run.noPermission")}</p>}
        <Button
          type="button"
          onClick={() => runMutation.mutate()}
          disabled={isBusy || !canRun || missingBlocking.length > 0}
        >
          {isBusy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          {phase === "saving" ? t("common:saving") : t("dataTransfer:mapping.saveAndContinue")}
        </Button>
      </div>
    </div>
  );
}
