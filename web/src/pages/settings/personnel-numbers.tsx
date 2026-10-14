import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import {
  Alert,
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Modal,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  getPersonnelNumberCompanies,
  getPersonnelNumberRanges,
  getPersonnelNumberRules,
  isPersonnelNumber,
  remainingPersonnelNumbers,
  savePersonnelNumberRange,
  savePersonnelNumberRule,
  setPersonnelNumberRangeActive,
  type PersonnelNumberRange,
  type PersonnelNumberRule,
  type PersonnelOperation,
} from "@/services/personnel-numbers.service";

const KEY = ["personnel-numbers"];
export default function PersonnelNumbersPage() {
  const canManage = usePermission("employee.personnel_number.manage");
  if (!canManage) return <NoAccess />;
  return <PersonnelNumbersAdmin canManage={canManage} />;
}
function PersonnelNumbersAdmin({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation(["settings", "common"]);
  const client = useQueryClient();
  const ranges = useQuery({ queryKey: [...KEY, "ranges"], queryFn: getPersonnelNumberRanges });
  const rules = useQuery({ queryKey: [...KEY, "rules"], queryFn: getPersonnelNumberRules });
  const [rangeTarget, setRangeTarget] = useState<PersonnelNumberRange | "new" | null>(null);
  const [ruleTarget, setRuleTarget] = useState<PersonnelNumberRule | "new" | null>(null);
  const toggle = useMutation({
    mutationFn: (r: PersonnelNumberRange) => setPersonnelNumberRangeActive(r.id, !r.isActive),
    onSuccess: () => void client.invalidateQueries({ queryKey: KEY }),
  });
  const loadError = ranges.error || rules.error || toggle.error;
  return (
    <Stack className="max-w-6xl p-6 md:p-10" gap="lg">
      <Title order={1} size="h2">
        {t("settings:personnelNumbers.title")}
      </Title>
      <Text c="dimmed">{t("settings:personnelNumbers.description")}</Text>
      {loadError && (
        <Alert color="red">
          <Text>{getApiErrorMessage(loadError, t("settings:personnelNumbers.loadError"))}</Text>
          <Button
            variant="light"
            onClick={() => {
              void ranges.refetch();
              void rules.refetch();
              toggle.reset();
            }}
          >
            {t("settings:personnelNumbers.retry")}
          </Button>
        </Alert>
      )}
      <Card withBorder>
        <Group justify="space-between" mb="md">
          <Title order={2} size="h4">
            {t("settings:personnelNumbers.ranges")}
          </Title>
          {canManage && (
            <Button onClick={() => setRangeTarget("new")}>
              {t("settings:personnelNumbers.addRange")}
            </Button>
          )}
        </Group>
        {ranges.isPending ? (
          <Text>{t("common:loading")}</Text>
        ) : !ranges.data?.length ? (
          <Text>{t("settings:personnelNumbers.empty")}</Text>
        ) : (
          <Stack>
            {ranges.data.map((range) => (
              <Card withBorder key={range.id}>
                <Group justify="space-between">
                  <Text fw={600}>
                    {range.code} · {range.name}
                  </Text>
                  <Badge color={range.isActive ? "green" : "gray"}>
                    {t(`settings:personnelNumbers.${range.isActive ? "active" : "inactive"}`)}
                  </Badge>
                </Group>
                <Text>
                  {range.startNumber}–{range.endNumber} ·{" "}
                  {t(`settings:personnelNumbers.${range.assignmentMode}`)}
                </Text>
                {range.assignmentMode === "internal" && (
                  <Text c={remainingPersonnelNumbers(range) ? "dimmed" : "red"}>
                    {t("settings:personnelNumbers.remaining", {
                      count: remainingPersonnelNumbers(range),
                      last: range.lastAssignedNumber ?? "—",
                    })}
                  </Text>
                )}
                {canManage && (
                  <Group justify="flex-end">
                    <Button variant="subtle" onClick={() => setRangeTarget(range)}>
                      {t("common:edit")}
                    </Button>
                    <Button
                      variant="subtle"
                      loading={toggle.isPending && toggle.variables?.id === range.id}
                      disabled={toggle.isPending}
                      onClick={() => toggle.mutate(range)}
                    >
                      {t(
                        `settings:personnelNumbers.${range.isActive ? "deactivate" : "reactivate"}`
                      )}
                    </Button>
                  </Group>
                )}
              </Card>
            ))}
          </Stack>
        )}
      </Card>
      <Card withBorder>
        <Group justify="space-between" mb="md">
          <Title order={2} size="h4">
            {t("settings:personnelNumbers.rules")}
          </Title>
          {canManage && (
            <Button
              disabled={!ranges.data?.some((r) => r.isActive)}
              onClick={() => setRuleTarget("new")}
            >
              {t("settings:personnelNumbers.addRule")}
            </Button>
          )}
        </Group>
        {rules.isPending ? (
          <Text>{t("common:loading")}</Text>
        ) : !rules.data?.length ? (
          <Text>{t("settings:personnelNumbers.empty")}</Text>
        ) : (
          <Stack>
            {rules.data.map((rule) => (
              <Card withBorder key={rule.id}>
                <Group justify="space-between">
                  <Text>
                    {rule.companyName} /{" "}
                    {rule.personnelAreaName ?? t("settings:personnelNumbers.companyDefault")} ·{" "}
                    {t(`settings:personnelNumbers.${rule.operation}`)} → {rule.rangeCode}
                  </Text>
                  <Badge color={rule.isActive ? "green" : "gray"}>
                    {t(`settings:personnelNumbers.${rule.isActive ? "active" : "inactive"}`)}
                  </Badge>
                  {canManage && (
                    <Button variant="subtle" onClick={() => setRuleTarget(rule)}>
                      {t("common:edit")}
                    </Button>
                  )}
                </Group>
              </Card>
            ))}
          </Stack>
        )}
      </Card>
      {rangeTarget && <RangeDialog target={rangeTarget} onClose={() => setRangeTarget(null)} />}
      {ruleTarget && (
        <RuleDialog
          target={ruleTarget}
          ranges={ranges.data ?? []}
          onClose={() => setRuleTarget(null)}
        />
      )}
    </Stack>
  );
}
function RangeDialog({
  target,
  onClose,
}: {
  target: PersonnelNumberRange | "new";
  onClose: () => void;
}) {
  const { t } = useTranslation(["settings", "common"]);
  const client = useQueryClient();
  const editing = target === "new" ? null : target;
  const [code, setCode] = useState(editing?.code ?? "");
  const [name, setName] = useState(editing?.name ?? "");
  const [start, setStart] = useState(editing?.startNumber ?? "");
  const [end, setEnd] = useState(editing?.endNumber ?? "");
  const [external, setExternal] = useState(editing?.assignmentMode === "external");
  const [validation, setValidation] = useState(false);
  const mutation = useMutation({
    mutationFn: () =>
      savePersonnelNumberRange(
        {
          code: code.trim(),
          name: name.trim(),
          startNumber: start,
          endNumber: end,
          assignmentMode: external ? "external" : "internal",
        },
        editing?.id
      ),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: KEY });
      onClose();
    },
  });
  function submit(e: FormEvent) {
    e.preventDefault();
    if (mutation.isPending) return;
    if (
      !code.trim() ||
      !name.trim() ||
      !isPersonnelNumber(start) ||
      !isPersonnelNumber(end) ||
      start > end ||
      (editing && end < editing.endNumber)
    ) {
      setValidation(true);
      return;
    }
    setValidation(false);
    mutation.mutate();
  }
  return (
    <Modal
      opened
      onClose={() => !mutation.isPending && onClose()}
      title={t(`settings:personnelNumbers.${editing ? "editRange" : "addRange"}`)}
    >
      <form onSubmit={submit}>
        <Stack>
          <TextInput
            label={t("settings:personnelNumbers.code")}
            value={code}
            onChange={(e) => setCode(e.currentTarget.value)}
            disabled={!!editing || mutation.isPending}
            maxLength={30}
            required
          />
          <TextInput
            label={t("settings:personnelNumbers.name")}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            disabled={mutation.isPending}
            maxLength={200}
            required
          />
          <TextInput
            label={t("settings:personnelNumbers.start")}
            value={start}
            onChange={(e) => setStart(e.currentTarget.value)}
            disabled={!!editing || mutation.isPending}
            maxLength={8}
            inputMode="numeric"
            required
          />
          <TextInput
            label={t("settings:personnelNumbers.end")}
            description={editing ? t("settings:personnelNumbers.extendOnly") : undefined}
            value={end}
            onChange={(e) => setEnd(e.currentTarget.value)}
            disabled={mutation.isPending}
            maxLength={8}
            inputMode="numeric"
            required
          />
          <Checkbox
            label={t("settings:personnelNumbers.external")}
            description={t("settings:personnelNumbers.modeHint")}
            checked={external}
            onChange={(e) => setExternal(e.currentTarget.checked)}
            disabled={!!editing || mutation.isPending}
          />
          {(validation || mutation.error) && (
            <Alert color="red">
              {mutation.error
                ? getApiErrorMessage(mutation.error, t("settings:personnelNumbers.saveError"))
                : t("settings:personnelNumbers.invalidRange")}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button variant="default" disabled={mutation.isPending} onClick={onClose}>
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
function RuleDialog({
  target,
  ranges,
  onClose,
}: {
  target: PersonnelNumberRule | "new";
  ranges: PersonnelNumberRange[];
  onClose: () => void;
}) {
  const { t } = useTranslation(["settings", "common"]);
  const client = useQueryClient();
  const editing = target === "new" ? null : target;
  const companies = useQuery({
    queryKey: [...KEY, "companies"],
    queryFn: getPersonnelNumberCompanies,
  });
  const [companyId, setCompanyId] = useState(editing?.companyId ?? "");
  const [areaId, setAreaId] = useState(editing?.personnelAreaId ?? "");
  const [operation, setOperation] = useState<PersonnelOperation>(editing?.operation ?? "hire");
  const [rangeId, setRangeId] = useState(editing?.rangeId ?? "");
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const available = ranges.filter((r) => r.isActive || r.id === editing?.rangeId);
  const validRange = available.some((r) => r.id === rangeId && (r.isActive || !isActive));
  const mutation = useMutation({
    mutationFn: () =>
      savePersonnelNumberRule(
        { companyId, personnelAreaId: areaId || null, operation, rangeId, isActive },
        editing?.id
      ),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: KEY });
      onClose();
    },
  });
  return (
    <Modal
      opened
      onClose={() => !mutation.isPending && onClose()}
      title={t(`settings:personnelNumbers.${editing ? "editRule" : "addRule"}`)}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (companyId && validRange && !mutation.isPending) mutation.mutate();
        }}
      >
        <Stack>
          {companies.isPending && <Text>{t("common:loading")}</Text>}
          {companies.error && (
            <Alert color="red">
              {getApiErrorMessage(companies.error, t("settings:personnelNumbers.loadError"))}
              <Button variant="light" onClick={() => void companies.refetch()}>
                {t("settings:personnelNumbers.retry")}
              </Button>
            </Alert>
          )}
          <Select
            label={t("settings:personnelNumbers.company")}
            data={(companies.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
            value={companyId || null}
            onChange={(v) => {
              setCompanyId(v ?? "");
              setAreaId("");
            }}
            disabled={!!editing || mutation.isPending}
            required
          />
          <Select
            label={t("settings:personnelNumbers.personnelArea")}
            data={[
              { value: "__default__", label: t("settings:personnelNumbers.companyDefault") },
              ...(companies.data?.find((c) => c.id === companyId)?.personnelAreas ?? []).map(
                (a) => ({ value: a.id, label: a.name })
              ),
            ]}
            value={areaId || "__default__"}
            onChange={(v) => setAreaId(v === "__default__" ? "" : (v ?? ""))}
            disabled={!!editing || !companyId || mutation.isPending}
          />
          <Select
            label={t("settings:personnelNumbers.operation")}
            description={operation === "import" ? t("settings:personnelNumbers.importHint") : undefined}
            data={["hire", "import"].map((value) => ({
              value,
              label: t(`settings:personnelNumbers.${value}`),
            }))}
            value={operation}
            onChange={(v) => {
              setOperation(v as PersonnelOperation);
              setRangeId("");
            }}
            disabled={!!editing || mutation.isPending}
            required
          />
          <Select
            label={t("settings:personnelNumbers.range")}
            data={available.map((r) => ({
              value: r.id,
              label: `${r.code} · ${r.name} · ${t(`settings:personnelNumbers.${r.assignmentMode}`)}`,
            }))}
            value={rangeId || null}
            onChange={(v) => setRangeId(v ?? "")}
            disabled={mutation.isPending}
            required
          />
          {editing && (
            <Switch
              label={t("settings:personnelNumbers.active")}
              checked={isActive}
              onChange={(e) => setIsActive(e.currentTarget.checked)}
              disabled={mutation.isPending}
            />
          )}
          {mutation.error && (
            <Alert color="red">
              {getApiErrorMessage(mutation.error, t("settings:personnelNumbers.saveError"))}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button variant="default" disabled={mutation.isPending} onClick={onClose}>
              {t("common:cancel")}
            </Button>
            <Button
              type="submit"
              loading={mutation.isPending}
              disabled={!companyId || !validRange || companies.isPending || companies.isError}
            >
              {t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
