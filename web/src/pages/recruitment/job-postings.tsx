import { useMemo, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, Briefcase, Play, Plus, Square } from "lucide-react";
import { Badge, Button, Card, Group, Modal, Select, Stack, Table, Text, Textarea, TextInput, Title, Tooltip, ActionIcon } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { useAuthStore } from "@/store/auth.store";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { listUsers } from "@/services/users.service";
import {
  createJobPosting,
  listJobPostings,
  publishJobPosting,
  closeJobPosting,
  archiveJobPosting,
  RECRUITMENT_EMPLOYMENT_TYPES,
  JOB_POSTING_ARCHIVE_REASONS,
  type JobPosting,
  type JobPostingArchiveReason,
  type RecruitmentEmploymentType,
} from "@/services/recruitment.service";

const POSTINGS_KEY = ["recruitment", "postings"] as const;

const schema = z.object({
  title: z.string().min(1),
  orgUnitId: z.string().min(1),
  hiringManagerUserId: z.string().optional(),
  employmentType: z.enum(RECRUITMENT_EMPLOYMENT_TYPES as [RecruitmentEmploymentType, ...RecruitmentEmploymentType[]]),
  description: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

const STATUS_COLOR: Record<JobPosting["status"], string> = {
  draft: "gray",
  open: "green",
  onHold: "yellow",
  closed: "orange",
  archived: "dark",
};

export default function JobPostingsPage() {
  const canRead = usePermission("recruitment.postings.read");
  if (!canRead) return <NoAccess />;
  return <JobPostings />;
}

function JobPostings() {
  const { t } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const currentUserId = useAuthStore((s) => s.user?.id);
  const canCreate = usePermission("recruitment.postings.create");
  const canUpdate = usePermission("recruitment.postings.update");
  const canArchive = usePermission("recruitment.postings.archive");

  const { data, isLoading, isError, error } = useQuery({ queryKey: POSTINGS_KEY, queryFn: () => listJobPostings() });
  const { options: orgUnitOptions, nameById: orgUnitNameById } = useOrgUnits();
  const { data: users } = useQuery({ queryKey: ["users", "options"], queryFn: () => listUsers({ pageSize: 200 }) });

  const [createOpen, setCreateOpen] = useState(false);
  const [archiveTarget, setArchiveTarget] = useState<JobPosting | null>(null);
  const [archiveReason, setArchiveReason] = useState<JobPostingArchiveReason>("positionFilled");

  const { register, handleSubmit, reset, control, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { title: "", orgUnitId: "", hiringManagerUserId: "", employmentType: "fullTime", description: "" },
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: POSTINGS_KEY });

  const createMutation = useMutation({
    mutationFn: (values: FormValues) =>
      createJobPosting({
        title: values.title,
        orgUnitId: values.orgUnitId,
        employmentType: values.employmentType,
        description: values.description || null,
        ownerUserId: currentUserId!,
        hiringManagerUserId: values.hiringManagerUserId || null,
      }),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:postings.createSuccess") });
      setCreateOpen(false);
      reset();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const publishMutation = useMutation({
    mutationFn: publishJobPosting,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:postings.publishSuccess") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const closeMutation = useMutation({
    mutationFn: closeJobPosting,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:postings.closeSuccess") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const archiveMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: JobPostingArchiveReason }) => archiveJobPosting(id, reason),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:postings.archiveSuccess") });
      setArchiveTarget(null);
    },
    onError: (err) => {
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) });
      setArchiveTarget(null);
    },
  });

  const orgUnitSelectOptions = useMemo(
    () => orgUnitOptions.map((u) => ({ value: u.id, label: `${"— ".repeat(u.depth)}${u.name}` })),
    [orgUnitOptions],
  );
  const userSelectOptions = useMemo(
    () => (users?.items ?? []).map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}` })),
    [users],
  );

  function openCreate() {
    reset({ title: "", orgUnitId: "", hiringManagerUserId: "", employmentType: "fullTime", description: "" });
    setCreateOpen(true);
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Briefcase size={24} /> {t("recruitment:postings.title")}
          </Title>
          <Text c="dimmed">{t("recruitment:postings.subtitle")}</Text>
        </div>
        {canCreate && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("recruitment:postings.addPosting")}
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
            {t("recruitment:postings.empty")}
          </Text>
        ) : (
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("recruitment:postings.titleField")}</Table.Th>
                <Table.Th>{t("recruitment:postings.orgUnit")}</Table.Th>
                <Table.Th>{t("recruitment:postings.employmentType")}</Table.Th>
                <Table.Th>{t("recruitment:postings.status")}</Table.Th>
                <Table.Th ta="right">{t("common:actions")}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((posting) => (
                <Table.Tr key={posting.id}>
                  <Table.Td>
                    <Link to={`/app/recruitment/postings/${posting.id}`} className="font-medium hover:underline">
                      {posting.title}
                    </Link>
                  </Table.Td>
                  <Table.Td>{orgUnitNameById.get(posting.orgUnitId) ?? "—"}</Table.Td>
                  <Table.Td>{t(`recruitment:employmentTypes.${posting.employmentType}`)}</Table.Td>
                  <Table.Td>
                    <Badge color={STATUS_COLOR[posting.status]} variant="light">
                      {t(`recruitment:postings.statuses.${posting.status}`)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} justify="flex-end" wrap="nowrap">
                      {canUpdate && (posting.status === "draft" || posting.status === "onHold" || posting.status === "closed") && (
                        <Tooltip label={t(posting.status === "closed" ? "recruitment:postings.reopen" : "recruitment:postings.publish")}>
                          <ActionIcon variant="subtle" color="green" onClick={() => publishMutation.mutate(posting.id)}>
                            <Play size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                      {canUpdate && posting.status === "open" && (
                        <Tooltip label={t("recruitment:postings.close")}>
                          <ActionIcon variant="subtle" color="orange" onClick={() => closeMutation.mutate(posting.id)}>
                            <Square size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                      {canArchive && posting.status !== "archived" && (
                        <Tooltip label={t("recruitment:postings.archive")}>
                          <ActionIcon variant="subtle" color="red" onClick={() => setArchiveTarget(posting)}>
                            <Archive size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title={t("recruitment:postings.dialogCreateTitle")}>
        <form onSubmit={handleSubmit((values) => createMutation.mutate(values))}>
          <Stack gap="md">
            <TextInput
              data-autofocus
              label={t("recruitment:postings.titleField")}
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
              name="hiringManagerUserId"
              render={({ field }) => (
                <Select
                  label={t("recruitment:postings.hiringManager")}
                  placeholder={t("recruitment:postings.hiringManagerNone")}
                  data={userSelectOptions}
                  value={field.value || null}
                  onChange={(value) => field.onChange(value ?? "")}
                  onBlur={field.onBlur}
                  searchable
                  clearable
                />
              )}
            />
            <Textarea label={t("recruitment:postings.description")} minRows={3} {...register("description")} />
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

      <Modal opened={archiveTarget !== null} onClose={() => setArchiveTarget(null)} title={t("recruitment:postings.archiveDialogTitle")}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("recruitment:postings.archiveDescription")}
          </Text>
          <Select
            label={t("recruitment:postings.archiveReason")}
            data={JOB_POSTING_ARCHIVE_REASONS.map((v) => ({ value: v, label: t(`recruitment:postings.archiveReasons.${v}`) }))}
            value={archiveReason}
            onChange={(value) => value && setArchiveReason(value as JobPostingArchiveReason)}
            allowDeselect={false}
          />
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={() => setArchiveTarget(null)} disabled={archiveMutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              color="red"
              loading={archiveMutation.isPending}
              onClick={() => archiveTarget && archiveMutation.mutate({ id: archiveTarget.id, reason: archiveReason })}
            >
              {t("recruitment:postings.archive")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  );
}
