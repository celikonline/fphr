import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Checkbox, Group, Input, Modal, Select, Skeleton, Stack, Text, TextInput, Title } from "@mantine/core";
import { Pencil, Plus, Trash2 } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  addUnionMembership,
  deleteUnionMembership,
  getUnionMemberships,
  updateUnionMembership,
  UNION_COVERAGE_TYPES,
  type EmployeeDetail,
  type UnionCoverageType,
  type UnionMembership,
  type UnionMembershipInput,
} from "@/services/employees.service";

/**
 * "Sendika bilgileri" (E5b): special-category personal data, so the whole block only renders with
 * employee.union.view and editing needs employee.union.manage.
 */
export function UnionSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const canView = usePermission("employee.union.view");
  const canManage = usePermission("employee.union.manage");
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<UnionMembership | "new" | null>(null);
  const [deleting, setDeleting] = useState<UnionMembership | null>(null);

  const { data: memberships = [], isLoading } = useQuery({
    queryKey: ["union-memberships", employee.id],
    queryFn: () => getUnionMemberships(employee.id),
    enabled: canView,
  });

  const deleteMutation = useMutation({
    mutationFn: (membership: UnionMembership) => deleteUnionMembership(employee.id, membership.id),
    onSuccess: () => {
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ["union-memberships", employee.id] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:union.deleted") });
    },
    onError: (error) => {
      setDeleting(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  if (!canView) return null;
  if (isLoading) return <Skeleton h={160} />;

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" wrap="nowrap" mb="md">
        <Title order={4}>{t("employees:union.title")}</Title>
        {canManage && (
          <Button type="button" size="sm" leftSection={<Plus size={16} />} onClick={() => setDialog("new")}>
            {t("employees:union.add")}
          </Button>
        )}
      </Group>

      {memberships.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:union.empty")}
        </Text>
      ) : (
        <Stack gap="md">
          {memberships.map((membership) => (
            <div key={membership.id} className="flex flex-wrap items-start justify-between gap-2">
              <Stack gap={4} className="min-w-0">
                <Group gap="xs" wrap="wrap">
                  <Text size="sm" fw={500}>
                    {membership.unionName}
                  </Text>
                  <Badge variant="light">{t(`employees:enum.unionCoverageType.${membership.coverageType}`)}</Badge>
                  {membership.isCurrent && <Badge variant="filled">{t("employees:union.current")}</Badge>}
                  {membership.isUnionOfficer && <Badge variant="outline">{t("employees:union.officer")}</Badge>}
                </Group>
                <Text size="xs" c="dimmed">
                  {t("employees:union.membershipDate")}: {date(membership.membershipDate)}
                  {" · "}
                  {t("employees:union.notifiedDate")}: {date(membership.employerNotifiedDate)}
                  {membership.endDate && ` · ${t("employees:union.endDate")}: ${date(membership.endDate)}`}
                  {membership.businessLineCode && ` · ${t("employees:union.businessLine")}: ${membership.businessLineCode}`}
                </Text>
              </Stack>
              {canManage && (
                <Group gap={4}>
                  <Button type="button" variant="subtle" size="sm" leftSection={<Pencil size={16} />} onClick={() => setDialog(membership)}>
                    {t("common:edit")}
                  </Button>
                  <Button type="button" variant="subtle" size="sm" leftSection={<Trash2 size={16} />} onClick={() => setDeleting(membership)}>
                    {t("common:delete")}
                  </Button>
                </Group>
              )}
            </div>
          ))}
        </Stack>
      )}

      {dialog && <UnionDialog employee={employee} editing={dialog === "new" ? null : dialog} onClose={() => setDialog(null)} />}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("employees:union.deleteTitle")}
        description={t("employees:union.deleteDescription")}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </Card>
  );
}

function UnionDialog({ employee, editing, onClose }: { employee: EmployeeDetail; editing: UnionMembership | null; onClose: () => void }) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();

  const [unionName, setUnionName] = useState(editing?.unionName ?? "");
  const [membershipDate, setMembershipDate] = useState(editing?.membershipDate ?? "");
  const [notifiedDate, setNotifiedDate] = useState(editing?.employerNotifiedDate ?? "");
  const [endDate, setEndDate] = useState(editing?.endDate ?? "");
  const [businessLineCode, setBusinessLineCode] = useState(editing?.businessLineCode ?? "");
  const [coverageType, setCoverageType] = useState<UnionCoverageType>(editing?.coverageType ?? "member");
  const [isUnionOfficer, setIsUnionOfficer] = useState(editing?.isUnionOfficer ?? false);
  const [officerSince, setOfficerSince] = useState(editing?.officerSince ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: UnionMembershipInput) =>
      editing ? updateUnionMembership(employee.id, editing.id, input) : addUnionMembership(employee.id, input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["union-memberships", employee.id] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:union.saved") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!unionName.trim()) return setError(t("employees:union.validation.nameRequired"));
    if (!membershipDate) return setError(t("employees:union.validation.membershipDateRequired"));

    setError(null);
    mutation.mutate({
      unionName: unionName.trim(),
      membershipDate,
      coverageType,
      businessLineCode: businessLineCode.trim() || null,
      employerNotifiedDate: notifiedDate || null,
      isUnionOfficer,
      officerSince: isUnionOfficer ? officerSince || null : null,
      endDate: endDate || null,
    });
  }

  return (
    <Modal opened onClose={onClose} title={editing ? t("employees:union.editTitle") : t("employees:union.add")} size="40rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("employees:union.dialogDescription")}
          </Text>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput
              id="union-name"
              label={t("employees:union.name")}
              withAsterisk
              value={unionName}
              maxLength={200}
              onChange={(event) => setUnionName(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              id="union-businessLine"
              label={t("employees:union.businessLine")}
              value={businessLineCode}
              maxLength={30}
              onChange={(event) => setBusinessLineCode(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <Input.Wrapper id="union-membershipDate" label={t("employees:union.membershipDate")} withAsterisk>
              <DatePicker id="union-membershipDate" value={membershipDate} onChange={setMembershipDate} disabled={mutation.isPending} />
            </Input.Wrapper>
            <Input.Wrapper
              id="union-notifiedDate"
              label={t("employees:union.notifiedDate")}
              description={t("employees:union.notifiedDateHint")}
              inputWrapperOrder={["label", "input", "description"]}
            >
              <DatePicker id="union-notifiedDate" value={notifiedDate} onChange={setNotifiedDate} clearable disabled={mutation.isPending} />
            </Input.Wrapper>
            <Select
              id="union-coverage"
              label={t("employees:union.coverageType")}
              data={UNION_COVERAGE_TYPES.map((option) => ({ value: option, label: t(`employees:enum.unionCoverageType.${option}`) }))}
              value={coverageType}
              onChange={(value) => value && setCoverageType(value as UnionCoverageType)}
              allowDeselect={false}
              disabled={mutation.isPending}
            />
            <Input.Wrapper id="union-endDate" label={t("employees:union.endDate")}>
              <DatePicker id="union-endDate" value={endDate} onChange={setEndDate} clearable disabled={mutation.isPending} />
            </Input.Wrapper>
          </div>
          <Checkbox
            label={t("employees:union.officer")}
            description={t("employees:union.officerHint")}
            checked={isUnionOfficer}
            onChange={(event) => setIsUnionOfficer(event.currentTarget.checked)}
            disabled={mutation.isPending}
          />
          {isUnionOfficer && (
            <Input.Wrapper id="union-officerSince" label={t("employees:union.officerSince")}>
              <DatePicker id="union-officerSince" value={officerSince} onChange={setOfficerSince} clearable disabled={mutation.isPending} />
            </Input.Wrapper>
          )}
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
