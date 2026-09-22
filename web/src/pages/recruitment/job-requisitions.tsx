import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardList, Plus, Send, X } from "lucide-react";
import { ActionIcon, Badge, Button, Card, Group, Modal, NumberInput, Select, Stack, Table, Text, Textarea, TextInput, Title, Tooltip } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { enumLabel } from "@/lib/i18n-enum";
import { useOrgUnits } from "@/components/employees/use-org-units";
import {
  cancelJobRequisition,
  createJobRequisition,
  listJobRequisitions,
  submitJobRequisition,
  RECRUITMENT_EMPLOYMENT_TYPES,
  REQUISITION_REASONS,
  type JobRequisition,
  type RecruitmentEmploymentType,
  type RequisitionReason,
} from "@/services/recruitment.service";

const REQUISITIONS_KEY = ["recruitment", "requisitions"] as const;

const schema = z.object({
  title: z.string().min(1),
  orgUnitId: z.string().min(1),
  employmentType: z.enum(RECRUITMENT_EMPLOYMENT_TYPES as [RecruitmentEmploymentType, ...RecruitmentEmploymentType[]]),
  headcountRequested: z.number().min(1),
  reason: z.enum(REQUISITION_REASONS as [RequisitionReason, ...RequisitionReason[]]),
  justification: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

const STATUS_COLOR: Record<JobRequisition["status"], string> = {
  draft: "gray",
  pendingApproval: "yellow",
  approved: "green",
  rejected: "red",
  cancelled: "dark",
};

export default function JobRequisitionsPage() {
  const canRead = usePermission("recruitment.requisitions.read");
  if (!canRead) return <NoAccess />;
  return <JobRequisitions />;
}

function JobRequisitions() {
  const { t } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("recruitment.requisitions.manage");
  const { options: orgUnitOptions, nameById: orgUnitNameById } = useOrgUnits();

  const { data, isLoading, isError, error } = useQuery({ queryKey: REQUISITIONS_KEY, queryFn: () => listJobRequisitions() });

  const [createOpen, setCreateOpen] = useState(false);
  const { register, handleSubmit, reset, control, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { title: "", orgUnitId: "", employmentType: "fullTime", headcountRequested: 1, reason: "newPosition", justification: "" },
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: REQUISITIONS_KEY });

  const createMutation = useMutation({
    mutationFn: createJobRequisition,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:requisitions.createSuccess") });
      setCreateOpen(false);
      reset();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const submitMutation = useMutation({
    mutationFn: submitJobRequisition,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:requisitions.submitSuccess") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const cancelMutation = useMutation({
    mutationFn: cancelJobRequisition,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:requisitions.cancelSuccess") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const orgUnitSelectOptions = useMemo(
    () => orgUnitOptions.map((u) => ({ value: u.id, label: `${"— ".repeat(u.depth)}${u.name}` })),
    [orgUnitOptions],
  );

  function openCreate() {
    reset({ title: "", orgUnitId: "", employmentType: "fullTime", headcountRequested: 1, reason: "newPosition", justification: "" });
    setCreateOpen(true);
  }

  function requisitionActions(requisition: JobRequisition) {
    return (
      <>
        {canManage && requisition.status === "draft" && (
          <Tooltip label={t("recruitment:requisitions.submit")}>
            <ActionIcon variant="subtle" color="green" onClick={() => submitMutation.mutate(requisition.id)}>
              <Send size={16} />
            </ActionIcon>
          </Tooltip>
        )}
        {canManage && (requisition.status === "draft" || requisition.status === "pendingApproval") && (
          <Tooltip label={t("recruitment:requisitions.cancel")}>
            <ActionIcon variant="subtle" color="red" onClick={() => cancelMutation.mutate(requisition.id)}>
              <X size={16} />
            </ActionIcon>
          </Tooltip>
        )}
      </>
    );
  }

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ClipboardList size={24} /> {t("recruitment:requisitions.title")}
          </Title>
          <Text c="dimmed">{t("recruitment:requisitions.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("recruitment:requisitions.addRequisition")}
          </Button>
        )}
      </Group>

      <Card withBorder padding={0}>
        {isLoading ? (
          <Text p="md" size="sm" c="dimmed">
            {t("common:loading")}
          </Text>
        ) : isError ? (
          <Text p="md" size="sm" c="red">
            {getApiErrorMessage(error, t("recruitment:genericError"))}
          </Text>
        ) : !data || data.length === 0 ? (
          <Text p="md" size="sm" c="dimmed">
            {t("recruitment:requisitions.empty")}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <Table striped highlightOnHover verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("recruitment:requisitions.titleField")}</Table.Th>
                    <Table.Th>{t("recruitment:postings.orgUnit")}</Table.Th>
                    <Table.Th>{t("recruitment:requisitions.headcount")}</Table.Th>
                    <Table.Th>{t("recruitment:requisitions.status")}</Table.Th>
                    <Table.Th ta="right">{t("common:actions")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {data.map((requisition) => (
                    <Table.Tr key={requisition.id}>
                      <Table.Td>
                        <Text fw={500}>{requisition.title}</Text>
                      </Table.Td>
                      <Table.Td>{orgUnitNameById.get(requisition.orgUnitId) ?? "—"}</Table.Td>
                      <Table.Td>{requisition.headcountRequested}</Table.Td>
                      <Table.Td>
                        <Badge color={STATUS_COLOR[requisition.status]} variant="light">
                          {enumLabel(t, "recruitment:requisitions.statuses", requisition.status)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Group gap={4} justify="flex-end" wrap="nowrap">
                          {requisitionActions(requisition)}
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </div>

            {/* Dar ekran: kart listesi */}
            <div className="md:hidden p-3">
              <Stack gap="sm">
                {data.map((requisition) => (
                  <Card key={requisition.id} withBorder padding="sm" radius="md">
                    <Stack gap={6}>
                      <Group justify="space-between" align="flex-start" wrap="nowrap">
                        <Text fw={500} size="sm">
                          {requisition.title}
                        </Text>
                        <Badge color={STATUS_COLOR[requisition.status]} variant="light">
                          {enumLabel(t, "recruitment:requisitions.statuses", requisition.status)}
                        </Badge>
                      </Group>
                      <Text size="xs" c="dimmed">
                        {orgUnitNameById.get(requisition.orgUnitId) ?? "—"} · {t("recruitment:requisitions.headcount")}: {requisition.headcountRequested}
                      </Text>
                      <Group gap={4} justify="flex-end" wrap="nowrap">
                        {requisitionActions(requisition)}
                      </Group>
                    </Stack>
                  </Card>
                ))}
              </Stack>
            </div>
          </>
        )}
      </Card>

      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title={t("recruitment:requisitions.dialogCreateTitle")}>
        <form onSubmit={handleSubmit((values) => createMutation.mutate(values))}>
          <Stack gap="md">
            <TextInput
              data-autofocus
              label={t("recruitment:requisitions.titleField")}
              error={errors.title ? t("common:required") : undefined}
              {...register("title")}
            />
            <Controller
              control={control}
              name="orgUnitId"
              render={({ field }) => (
                <Select
                  label={t("recruitment:postings.orgUnit")}
                  data={orgUnitSelectOptions}
                  value={field.value}
                  onChange={(value) => field.onChange(value ?? "")}
                  onBlur={field.onBlur}
                  searchable
                  error={errors.orgUnitId ? t("common:required") : undefined}
                />
              )}
            />
            <Controller
              control={control}
              name="employmentType"
              render={({ field }) => (
                <Select
                  label={t("recruitment:postings.employmentType")}
                  data={RECRUITMENT_EMPLOYMENT_TYPES.map((v) => ({ value: v, label: t(`recruitment:employmentTypes.${v}`) }))}
                  value={field.value}
                  onChange={(value) => value && field.onChange(value as RecruitmentEmploymentType)}
                  onBlur={field.onBlur}
                  allowDeselect={false}
                />
              )}
            />
            <Controller
              control={control}
              name="headcountRequested"
              render={({ field }) => (
                <NumberInput
                  label={t("recruitment:requisitions.headcount")}
                  min={1}
                  value={field.value}
                  onChange={(value) => field.onChange(typeof value === "number" ? value : 1)}
                  onBlur={field.onBlur}
                  error={errors.headcountRequested ? t("common:required") : undefined}
                />
              )}
            />
            <Controller
              control={control}
              name="reason"
              render={({ field }) => (
                <Select
                  label={t("recruitment:requisitions.reason")}
                  data={REQUISITION_REASONS.map((v) => ({ value: v, label: t(`recruitment:requisitions.reasons.${v}`) }))}
                  value={field.value}
                  onChange={(value) => value && field.onChange(value as RequisitionReason)}
                  onBlur={field.onBlur}
                  allowDeselect={false}
                />
              )}
            />
            <Textarea label={t("recruitment:requisitions.justification")} minRows={3} {...register("justification")} />
            <Group justify="flex-end" gap="sm">
              <Button type="button" variant="default" onClick={() => setCreateOpen(false)} disabled={createMutation.isPending}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={createMutation.isPending}>
                {createMutation.isPending ? t("common:saving") : t("common:save")}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </div>
  );
}
