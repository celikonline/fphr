import { useMemo, useState } from "react";
import { Link, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Briefcase, Check, ClipboardCheck, Copy, ExternalLink, Plus, UserPlus } from "lucide-react";
import {
  Anchor,
  Badge,
  Button,
  Card,
  Checkbox,
  CopyButton,
  Divider,
  Group,
  Modal,
  MultiSelect,
  NumberInput,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { enumLabel } from "@/lib/i18n-enum";
import { formatDateTime } from "@/lib/dates";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { listUsers } from "@/services/users.service";
import {
  createCandidate,
  createJobApplication,
  getJobPosting,
  getJobPostingAccess,
  getJobRequisition,
  listApplicationsByPosting,
  listCandidates,
  listFormResponsesByApplication,
  listFormTemplates,
  moveJobApplicationStage,
  setJobPostingAccess,
  submitFormResponse,
  APPLICATION_STAGES,
  type ApplicationStage,
  type Candidate,
  type FieldAnswerInput,
  type JobPosting,
} from "@/services/recruitment.service";

const STATUS_COLOR: Record<JobPosting["status"], string> = {
  draft: "gray",
  open: "green",
  onHold: "yellow",
  closed: "orange",
  archived: "dark",
};

const newCandidateSchema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
});
type NewCandidateValues = z.infer<typeof newCandidateSchema>;

export default function JobPostingDetailPage() {
  const canRead = usePermission("recruitment.postings.read");
  if (!canRead) return <NoAccess />;
  return <JobPostingDetail />;
}

function JobPostingDetail() {
  const { id } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const canManageApplications = usePermission("recruitment.applications.manage");
  const canSubmitForms = usePermission("recruitment.form_responses.submit");
  const canManageAccess = usePermission("recruitment.postings.update");
  const { nameById: orgUnitNameById } = useOrgUnits();

  const postingQuery = useQuery({
    queryKey: ["recruitment", "postings", id],
    queryFn: () => getJobPosting(id!),
    enabled: Boolean(id),
  });
  const requisitionId = postingQuery.data?.requisitionId;
  const requisitionQuery = useQuery({
    queryKey: ["recruitment", "requisitions", requisitionId],
    queryFn: () => getJobRequisition(requisitionId!),
    enabled: Boolean(requisitionId),
  });
  const applicationsQuery = useQuery({
    queryKey: ["recruitment", "applications", "by-posting", id],
    queryFn: () => listApplicationsByPosting(id!),
    enabled: Boolean(id),
  });
  const candidatesQuery = useQuery({ queryKey: ["recruitment", "candidates"], queryFn: () => listCandidates() });
  const templatesQuery = useQuery({ queryKey: ["recruitment", "form-templates", "active"], queryFn: () => listFormTemplates(true) });

  const candidateById = useMemo(() => new Map((candidatesQuery.data ?? []).map((c: Candidate) => [c.id, c])), [candidatesQuery.data]);

  const invalidateApplications = () => queryClient.invalidateQueries({ queryKey: ["recruitment", "applications", "by-posting", id] });

  const [addOpen, setAddOpen] = useState(false);
  const [pickCandidateId, setPickCandidateId] = useState<string | null>(null);
  const [creatingNew, setCreatingNew] = useState(false);

  const { register, handleSubmit, reset, formState: { errors } } = useForm<NewCandidateValues>({
    resolver: zodResolver(newCandidateSchema),
    defaultValues: { firstName: "", lastName: "", email: "", phone: "" },
  });

  const applyMutation = useMutation({
    mutationFn: (candidateId: string) => createJobApplication(id!, candidateId),
    onSuccess: () => {
      invalidateApplications();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:applications.createSuccess") });
      closeAddDialog();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const createAndApplyMutation = useMutation({
    mutationFn: async (values: NewCandidateValues) => {
      const candidateId = await createCandidate({ ...values, phone: values.phone || null });
      await createJobApplication(id!, candidateId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["recruitment", "candidates"] });
      invalidateApplications();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:applications.createSuccess") });
      closeAddDialog();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const stageMutation = useMutation({
    mutationFn: ({ applicationId, stage }: { applicationId: string; stage: ApplicationStage }) => moveJobApplicationStage(applicationId, stage),
    onSuccess: () => {
      invalidateApplications();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:applications.stageChanged") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const [formApplicationId, setFormApplicationId] = useState<string | null>(null);
  const [formTemplateId, setFormTemplateId] = useState<string | null>(null);
  const [formAnswers, setFormAnswers] = useState<Record<string, string>>({});

  const responsesQuery = useQuery({
    queryKey: ["recruitment", "form-responses", formApplicationId],
    queryFn: () => listFormResponsesByApplication(formApplicationId!),
    enabled: Boolean(formApplicationId),
  });

  const submitFormMutation = useMutation({
    mutationFn: () => {
      const answers: FieldAnswerInput[] = Object.entries(formAnswers).map(([fieldId, value]) => ({ fieldId, value: value || null }));
      return submitFormResponse(formTemplateId!, formApplicationId!, answers);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["recruitment", "form-responses", formApplicationId] });
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:applications.formSubmitted") });
      setFormTemplateId(null);
      setFormAnswers({});
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const selectedTemplate = (templatesQuery.data ?? []).find((tpl) => tpl.id === formTemplateId);
  const templateOptions = (templatesQuery.data ?? []).map((tpl) => ({ value: tpl.id, label: tpl.name }));

  function closeAddDialog() {
    setAddOpen(false);
    setPickCandidateId(null);
    setCreatingNew(false);
    reset();
  }

  const candidateOptions = (candidatesQuery.data ?? []).map((c) => ({ value: c.id, label: `${c.firstName} ${c.lastName} (${c.email})` }));

  if (postingQuery.isLoading) {
    return (
      <div className="max-w-5xl p-6 md:p-10">
        <Text c="dimmed">{t("common:loading")}</Text>
      </div>
    );
  }

  if (postingQuery.isError || !postingQuery.data) {
    return (
      <div className="max-w-5xl p-6 md:p-10">
        <Text c="red">{getApiErrorMessage(postingQuery.error, t("recruitment:genericError"))}</Text>
      </div>
    );
  }

  const posting = postingQuery.data;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <Link to="/app/recruitment/postings" className="inline-flex items-center gap-1 text-sm text-gray-500 hover:underline">
        <ArrowLeft size={14} /> {t("recruitment:postings.title")}
      </Link>

      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Briefcase size={24} /> {posting.title}
          </Title>
          <Group gap="xs" mt={4}>
            <Badge color={STATUS_COLOR[posting.status]} variant="light">
              {enumLabel(t, "recruitment:postings.statuses", posting.status)}
            </Badge>
            <Text c="dimmed" size="sm">
              {orgUnitNameById.get(posting.orgUnitId) ?? "—"} · {enumLabel(t, "recruitment:employmentTypes", posting.employmentType)}
            </Text>
          </Group>
          {posting.status === "open" && (
            <Group gap="xs" mt={8}>
              <CopyButton value={`${window.location.origin}/kariyer/${posting.applySlug}`}>
                {({ copied, copy }) => (
                  <Button
                    size="xs"
                    variant="default"
                    color={copied ? "teal" : undefined}
                    leftSection={copied ? <Check size={14} /> : <Copy size={14} />}
                    onClick={copy}
                  >
                    {copied ? t("recruitment:postingDetail.careerLink.copied") : t("recruitment:postingDetail.careerLink.copy")}
                  </Button>
                )}
              </CopyButton>
              <Anchor
                href={`/kariyer/${posting.applySlug}`}
                target="_blank"
                rel="noopener noreferrer"
                size="sm"
                className="inline-flex items-center gap-1"
              >
                {t("recruitment:postingDetail.careerLink.openInCareer")} <ExternalLink size={12} />
              </Anchor>
            </Group>
          )}
        </div>
      </Group>

      {requisitionQuery.data && requisitionQuery.data.status !== "approved" && (
        <Card withBorder bg="var(--mantine-color-yellow-0)">
          <Text size="sm">
            {t("recruitment:postings.requisitionPendingWarning", { status: enumLabel(t, "recruitment:requisitions.statuses", requisitionQuery.data.status) })}
          </Text>
        </Card>
      )}

      {posting.description && (
        <Card withBorder>
          <Text size="sm">{posting.description}</Text>
        </Card>
      )}

      <JobPostingAccessSection postingId={posting.id} canManageAccess={canManageAccess} />

      <Divider label={t("recruitment:applications.title")} labelPosition="left" />

      <Group justify="flex-end">
        {canManageApplications && (
          <Button type="button" leftSection={<UserPlus size={16} />} onClick={() => setAddOpen(true)}>
            {t("recruitment:applications.addApplication")}
          </Button>
        )}
      </Group>

      <Card withBorder padding={0}>
        {applicationsQuery.isLoading ? (
          <Text p="md" size="sm" c="dimmed">
            {t("common:loading")}
          </Text>
        ) : !applicationsQuery.data || applicationsQuery.data.length === 0 ? (
          <Text p="md" size="sm" c="dimmed">
            {t("recruitment:applications.empty")}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <Table striped highlightOnHover verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("recruitment:applications.candidate")}</Table.Th>
                    <Table.Th>{t("recruitment:applications.appliedAt")}</Table.Th>
                    <Table.Th>{t("recruitment:applications.stage")}</Table.Th>
                    {canSubmitForms && <Table.Th>{t("recruitment:applications.forms")}</Table.Th>}
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {applicationsQuery.data.map((application) => {
                    const candidate = candidateById.get(application.candidateId);
                    const isTerminal = application.stage === "hired" || application.stage === "rejected";
                    const isAnonymized = candidate?.isAnonymized ?? false;
                    return (
                      <Table.Tr key={application.id} opacity={isAnonymized ? 0.6 : 1}>
                        <Table.Td>
                          <Group gap={6} wrap="nowrap">
                            <Text>{candidate ? `${candidate.firstName} ${candidate.lastName}` : "—"}</Text>
                            {isAnonymized && (
                              <Tooltip label={t("recruitment:candidates.anonymizedRowHint")}>
                                <Badge size="sm" color="gray" variant="light">
                                  {t("recruitment:candidates.anonymized")}
                                </Badge>
                              </Tooltip>
                            )}
                          </Group>
                        </Table.Td>
                        <Table.Td>{formatDateTime(application.appliedAt, i18n.language)}</Table.Td>
                        <Table.Td className="max-w-[220px]">
                          {isAnonymized ? (
                            <Text size="sm" c="dimmed">
                              {enumLabel(t, "recruitment:applications.stages", application.stage)}
                            </Text>
                          ) : (
                            <Select
                              data={APPLICATION_STAGES.map((s) => ({ value: s, label: t(`recruitment:applications.stages.${s}`) }))}
                              value={application.stage}
                              disabled={!canManageApplications || isTerminal}
                              allowDeselect={false}
                              onChange={(value) => value && stageMutation.mutate({ applicationId: application.id, stage: value as ApplicationStage })}
                            />
                          )}
                        </Table.Td>
                        {canSubmitForms && (
                          <Table.Td>
                            <Button
                              size="xs"
                              variant="subtle"
                              leftSection={<ClipboardCheck size={14} />}
                              onClick={() => {
                                setFormApplicationId(application.id);
                                setFormTemplateId(null);
                                setFormAnswers({});
                              }}
                            >
                              {t("recruitment:applications.fillForm")}
                            </Button>
                          </Table.Td>
                        )}
                      </Table.Tr>
                    );
                  })}
                </Table.Tbody>
              </Table>
            </div>

            {/* Dar ekran: kart listesi */}
            <div className="md:hidden p-3">
              <Stack gap="sm">
                {applicationsQuery.data.map((application) => {
                  const candidate = candidateById.get(application.candidateId);
                  const isTerminal = application.stage === "hired" || application.stage === "rejected";
                  const isAnonymized = candidate?.isAnonymized ?? false;
                  return (
                    <Card key={application.id} withBorder padding="sm" radius="md" opacity={isAnonymized ? 0.6 : 1}>
                      <Stack gap={6}>
                        <Group gap={6} wrap="nowrap" justify="space-between">
                          <Text fw={500} size="sm">
                            {candidate ? `${candidate.firstName} ${candidate.lastName}` : "—"}
                          </Text>
                          {isAnonymized && (
                            <Badge size="sm" color="gray" variant="light">
                              {t("recruitment:candidates.anonymized")}
                            </Badge>
                          )}
                        </Group>
                        <Text size="xs" c="dimmed">
                          {formatDateTime(application.appliedAt, i18n.language)}
                        </Text>
                        {isAnonymized ? (
                          <Badge size="sm" variant="light">
                            {enumLabel(t, "recruitment:applications.stages", application.stage)}
                          </Badge>
                        ) : (
                          <Select
                            label={t("recruitment:applications.stage")}
                            data={APPLICATION_STAGES.map((s) => ({ value: s, label: t(`recruitment:applications.stages.${s}`) }))}
                            value={application.stage}
                            disabled={!canManageApplications || isTerminal}
                            allowDeselect={false}
                            onChange={(value) => value && stageMutation.mutate({ applicationId: application.id, stage: value as ApplicationStage })}
                          />
                        )}
                        {canSubmitForms && (
                          <Button
                            size="xs"
                            variant="subtle"
                            leftSection={<ClipboardCheck size={14} />}
                            onClick={() => {
                              setFormApplicationId(application.id);
                              setFormTemplateId(null);
                              setFormAnswers({});
                            }}
                          >
                            {t("recruitment:applications.fillForm")}
                          </Button>
                        )}
                      </Stack>
                    </Card>
                  );
                })}
              </Stack>
            </div>
          </>
        )}
      </Card>

      <Modal opened={addOpen} onClose={closeAddDialog} title={t("recruitment:applications.dialogTitle")}>
        <Stack gap="md">
          {!creatingNew ? (
            <>
              <Select
                label={t("recruitment:applications.pickExisting")}
                data={candidateOptions}
                value={pickCandidateId}
                onChange={setPickCandidateId}
                searchable
                clearable
              />
              <Button type="button" variant="subtle" leftSection={<Plus size={14} />} onClick={() => setCreatingNew(true)}>
                {t("recruitment:applications.orCreateNew")}
              </Button>
              <Group justify="flex-end" gap="sm">
                <Button type="button" variant="default" onClick={closeAddDialog} disabled={applyMutation.isPending}>
                  {t("common:cancel")}
                </Button>
                <Button
                  type="button"
                  disabled={!pickCandidateId || applyMutation.isPending}
                  onClick={() => pickCandidateId && applyMutation.mutate(pickCandidateId)}
                >
                  {applyMutation.isPending ? t("common:saving") : t("common:save")}
                </Button>
              </Group>
            </>
          ) : (
            <form onSubmit={handleSubmit((values) => createAndApplyMutation.mutate(values))}>
              <Stack gap="md">
                <TextInput
                  data-autofocus
                  label={t("recruitment:candidates.firstName")}
                  error={errors.firstName ? t("common:required") : undefined}
                  {...register("firstName")}
                />
                <TextInput
                  label={t("recruitment:candidates.lastName")}
                  error={errors.lastName ? t("common:required") : undefined}
                  {...register("lastName")}
                />
                <TextInput
                  label={t("recruitment:candidates.email")}
                  error={errors.email ? t("common:required") : undefined}
                  {...register("email")}
                />
                <TextInput label={t("recruitment:candidates.phone")} {...register("phone")} />
                <Group justify="flex-end" gap="sm">
                  <Button type="button" variant="default" onClick={() => setCreatingNew(false)} disabled={createAndApplyMutation.isPending}>
                    {t("common:cancel")}
                  </Button>
                  <Button type="submit" disabled={createAndApplyMutation.isPending}>
                    {createAndApplyMutation.isPending ? t("common:saving") : t("common:save")}
                  </Button>
                </Group>
              </Stack>
            </form>
          )}
        </Stack>
      </Modal>

      <Modal opened={formApplicationId !== null} onClose={() => setFormApplicationId(null)} title={t("recruitment:applications.fillForm")} size="lg">
        <Stack gap="md">
          {(() => {
            const app = applicationsQuery.data?.find(a => a.id === formApplicationId);
            const cand = app ? candidateById.get(app.candidateId) : null;
            if (cand?.isAnonymized) {
              return (
                <Card withBorder bg="var(--mantine-color-yellow-0)">
                  <Text size="sm" c="orange">
                    {t("recruitment:applications.formResponsesCleaned")}
                  </Text>
                </Card>
              );
            }
            return null;
          })()}

          {responsesQuery.data && responsesQuery.data.length > 0 && (
            <Text size="sm" c="dimmed">
              {t("recruitment:applications.responsesSubmitted", { count: responsesQuery.data.length })}
            </Text>
          )}

          <Select
            label={t("recruitment:applications.selectTemplate")}
            data={templateOptions}
            value={formTemplateId}
            onChange={(value) => {
              setFormTemplateId(value);
              setFormAnswers({});
            }}
            searchable
          />

          {selectedTemplate && (
            <Stack gap="sm">
              {selectedTemplate.fields.map((field) => {
                const value = formAnswers[field.id] ?? "";
                const setValue = (v: string) => setFormAnswers((prev) => ({ ...prev, [field.id]: v }));
                const label = `${field.label}${field.isRequired ? " *" : ""}`;

                if (field.fieldType === "scoreCard") {
                  return (
                    <NumberInput
                      key={field.id}
                      label={label}
                      min={0}
                      max={100}
                      value={value === "" ? undefined : Number(value)}
                      onChange={(v) => setValue(v === "" || v === undefined ? "" : String(v))}
                    />
                  );
                }

                if (field.fieldType === "dropdown") {
                  return (
                    <Select
                      key={field.id}
                      label={label}
                      data={field.options}
                      value={value || null}
                      onChange={(v) => setValue(v ?? "")}
                    />
                  );
                }

                if (field.fieldType === "checkbox") {
                  return (
                    <Checkbox key={field.id} label={label} checked={value === "true"} onChange={(e) => setValue(e.currentTarget.checked ? "true" : "false")} />
                  );
                }

                if (field.fieldType === "date") {
                  return <TextInput key={field.id} type="date" label={label} value={value} onChange={(e) => setValue(e.currentTarget.value)} />;
                }

                return <TextInput key={field.id} label={label} value={value} onChange={(e) => setValue(e.currentTarget.value)} />;
              })}
            </Stack>
          )}

          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={() => setFormApplicationId(null)} disabled={submitFormMutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="button" disabled={!formTemplateId || submitFormMutation.isPending} onClick={() => submitFormMutation.mutate()}>
              {submitFormMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  );
}

/** Kimlerin bu ilana erişebileceğini gösterir (TK-023); `canManageAccess` yoksa salt okunur liste. */
function JobPostingAccessSection({ postingId, canManageAccess }: { postingId: string; canManageAccess: boolean }) {
  const { t } = useTranslation(["recruitment", "common"]);

  const accessQuery = useQuery({
    queryKey: ["recruitment", "postings", postingId, "access"],
    queryFn: () => getJobPostingAccess(postingId),
  });
  const usersQuery = useQuery({ queryKey: ["users", "options"], queryFn: () => listUsers({ pageSize: 200 }) });

  const userOptions = useMemo(
    () => (usersQuery.data?.items ?? []).map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}` })),
    [usersQuery.data],
  );
  const userNameById = useMemo(() => new Map(userOptions.map((o) => [o.value, o.label])), [userOptions]);

  return (
    <Card withBorder>
      <Text fw={600} mb={4}>
        {t("recruitment:postings.access")}
      </Text>
      <Text size="sm" c="dimmed" mb="sm">
        {t("recruitment:postings.accessHint")}
      </Text>

      {accessQuery.isLoading ? (
        <Text size="sm" c="dimmed">
          {t("common:loading")}
        </Text>
      ) : canManageAccess ? (
        <JobPostingAccessEditor postingId={postingId} initialUserIds={accessQuery.data ?? []} userOptions={userOptions} />
      ) : (accessQuery.data ?? []).length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("recruitment:postings.accessEmpty")}
        </Text>
      ) : (
        <Group gap="xs">
          {(accessQuery.data ?? []).map((userId) => (
            <Badge key={userId} variant="light">
              {userNameById.get(userId) ?? userId}
            </Badge>
          ))}
        </Group>
      )}
    </Card>
  );
}

/** `canManageAccess` altında düzenlenebilir liste; kaydet `SetJobPostingAccessCommand`'a giden PUT'u tetikler. */
function JobPostingAccessEditor({
  postingId,
  initialUserIds,
  userOptions,
}: {
  postingId: string;
  initialUserIds: string[];
  userOptions: { value: string; label: string }[];
}) {
  const { t } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const [selectedUserIds, setSelectedUserIds] = useState<string[]>(initialUserIds);

  const saveMutation = useMutation({
    mutationFn: () => setJobPostingAccess(postingId, selectedUserIds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["recruitment", "postings", postingId, "access"] });
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:postings.accessSaved") });
    },
    onError: (err) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  return (
    <Stack gap="sm">
      <MultiSelect
        aria-label={t("recruitment:postings.access")}
        data={userOptions}
        value={selectedUserIds}
        onChange={setSelectedUserIds}
        searchable
        clearable
      />
      <Group justify="flex-end">
        <Button type="button" size="sm" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate()}>
          {saveMutation.isPending ? t("common:saving") : t("common:save")}
        </Button>
      </Group>
    </Stack>
  );
}
