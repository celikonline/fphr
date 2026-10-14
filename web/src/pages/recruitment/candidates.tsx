import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import {
  Archive,
  ClipboardList,
  LifeBuoy,
  Pencil,
  Plus,
  Search,
  ShieldOff,
  Tags as TagsIcon,
  UserRound,
} from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Drawer,
  Group,
  Modal,
  MultiSelect,
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
import { formatDateTime } from "@/lib/dates";
import { enumLabel } from "@/lib/i18n-enum";
import {
  anonymizeCandidate,
  anonymizeExpiredCandidates,
  createCandidate,
  createTag,
  listApplicationsByCandidate,
  listCandidates,
  listJobPostings,
  listTags,
  setCandidateTags,
  updateCandidate,
  type Candidate,
} from "@/services/recruitment.service";

const CANDIDATES_KEY = ["recruitment", "candidates"] as const;

const schema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

interface DialogState {
  open: boolean;
  candidate?: Candidate;
}

export default function CandidatesPage() {
  const canRead = usePermission("recruitment.candidates.read");
  if (!canRead) return <NoAccess />;
  return <Candidates />;
}

function Candidates() {
  const { t, i18n } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("recruitment.candidates.manage");

  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: [...CANDIDATES_KEY, debouncedSearch],
    queryFn: () => listCandidates(debouncedSearch || undefined),
    placeholderData: (previous) => previous,
  });
  const tagsQuery = useQuery({ queryKey: ["recruitment", "tags"], queryFn: () => listTags() });
  const tagNameById = new Map((tagsQuery.data ?? []).map((tag) => [tag.id, tag.name]));

  const [dialog, setDialog] = useState<DialogState>({ open: false });
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: "", lastName: "", email: "", phone: "" },
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: CANDIDATES_KEY });

  const createMutation = useMutation({
    mutationFn: createCandidate,
    onSuccess: () => {
      invalidate();
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("recruitment:candidates.createSuccess"),
      });
      setDialog({ open: false });
    },
    onError: (err) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("recruitment:genericError")),
      }),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...input }: { id: string } & FormValues) => updateCandidate(id, input),
    onSuccess: () => {
      invalidate();
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("recruitment:candidates.updateSuccess"),
      });
      setDialog({ open: false });
    },
    onError: (err) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("recruitment:genericError")),
      }),
  });

  const [anonymizeTarget, setAnonymizeTarget] = useState<Candidate | null>(null);
  const anonymizeMutation = useMutation({
    mutationFn: anonymizeCandidate,
    onSuccess: () => {
      invalidate();
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("recruitment:candidates.anonymizeSuccess"),
      });
      setAnonymizeTarget(null);
    },
    onError: (err) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("recruitment:genericError")),
      }),
  });

  const [anonymizeExpiredOpen, setAnonymizeExpiredOpen] = useState(false);
  const anonymizeExpiredMutation = useMutation({
    mutationFn: anonymizeExpiredCandidates,
    onSuccess: (count) => {
      invalidate();
      toast({
        variant: "success",
        title: t("common:success"),
        description:
          count > 0
            ? t("recruitment:candidates.anonymizeExpired.successCount", { count })
            : t("recruitment:candidates.anonymizeExpired.successEmpty"),
      });
      setAnonymizeExpiredOpen(false);
    },
    onError: (err) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("recruitment:genericError")),
      }),
  });

  const [applicationsCandidate, setApplicationsCandidate] = useState<Candidate | null>(null);
  const applicationsQuery = useQuery({
    queryKey: ["recruitment", "candidates", applicationsCandidate?.id, "applications"],
    queryFn: () => listApplicationsByCandidate(applicationsCandidate!.id),
    enabled: applicationsCandidate !== null,
  });
  const postingsQuery = useQuery({
    queryKey: ["recruitment", "postings"],
    queryFn: () => listJobPostings(),
  });
  const postingTitleById = new Map((postingsQuery.data ?? []).map((p) => [p.id, p.title]));

  const [tagCandidate, setTagCandidate] = useState<Candidate | null>(null);
  const [selectedTagIds, setSelectedTagIds] = useState<string[]>([]);
  const [newTagName, setNewTagName] = useState("");

  const setTagsMutation = useMutation({
    mutationFn: ({ id, tagIds }: { id: string; tagIds: string[] }) => setCandidateTags(id, tagIds),
    onSuccess: () => {
      invalidate();
      setTagCandidate(null);
    },
    onError: (err) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("recruitment:genericError")),
      }),
  });

  const createTagMutation = useMutation({
    mutationFn: createTag,
    onSuccess: (id) => {
      queryClient.invalidateQueries({ queryKey: ["recruitment", "tags"] });
      setSelectedTagIds((prev) => [...prev, id]);
      setNewTagName("");
    },
    onError: (err) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("recruitment:genericError")),
      }),
  });

  function openTagEditor(candidate: Candidate) {
    setSelectedTagIds(candidate.tagIds);
    setTagCandidate(candidate);
  }

  function openCreate() {
    reset({ firstName: "", lastName: "", email: "", phone: "" });
    setDialog({ open: true });
  }

  function openEdit(candidate: Candidate) {
    reset({
      firstName: candidate.firstName,
      lastName: candidate.lastName,
      email: candidate.email,
      phone: candidate.phone ?? "",
    });
    setDialog({ open: true, candidate });
  }

  function candidateActions(candidate: Candidate) {
    return (
      <>
        <Tooltip label={t("recruitment:candidates.applications.actionLabel")}>
          <ActionIcon
            variant="subtle"
            color="gray"
            onClick={() => setApplicationsCandidate(candidate)}
          >
            <ClipboardList size={16} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t("recruitment:candidates.editTags")}>
          <ActionIcon variant="subtle" color="gray" onClick={() => openTagEditor(candidate)}>
            <TagsIcon size={16} />
          </ActionIcon>
        </Tooltip>
        {!candidate.isAnonymized && (
          <>
            <Tooltip label={t("recruitment:candidates.editCandidate")}>
              <ActionIcon variant="subtle" color="gray" onClick={() => openEdit(candidate)}>
                <Pencil size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label={t("recruitment:candidates.anonymize")}>
              <ActionIcon
                variant="subtle"
                color="red"
                onClick={() => setAnonymizeTarget(candidate)}
              >
                <ShieldOff size={16} />
              </ActionIcon>
            </Tooltip>
          </>
        )}
      </>
    );
  }

  function onSubmit(values: FormValues) {
    if (dialog.candidate) {
      updateMutation.mutate({ id: dialog.candidate.id, ...values });
    } else {
      createMutation.mutate(values);
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending;

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <UserRound size={24} /> {t("recruitment:candidates.title")}
          </Title>
          <Text c="dimmed">{t("recruitment:candidates.subtitle")}</Text>
        </div>
        {canManage && (
          <Group gap="sm">
            <Button
              type="button"
              variant="default"
              leftSection={<Archive size={16} />}
              onClick={() => setAnonymizeExpiredOpen(true)}
            >
              {t("recruitment:candidates.anonymizeExpired.buttonLabel")}
            </Button>
            <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
              {t("recruitment:candidates.addCandidate")}
            </Button>
          </Group>
        )}
      </Group>

      <TextInput
        className="w-full max-w-sm"
        leftSection={<Search size={16} />}
        placeholder={t("recruitment:candidates.searchPlaceholder")}
        value={search}
        onChange={(e) => setSearch(e.currentTarget.value)}
      />

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
          <Stack align="center" gap="sm" py="xl" px="md">
            <Text size="sm" c="dimmed" ta="center">
              {search.trim()
                ? t("recruitment:candidates.emptySearch")
                : t("recruitment:candidates.empty")}
            </Text>
            <Text size="sm" c="dimmed" ta="center">
              {t(search.trim() ? "recruitment:candidates.emptySearchGuide" : "recruitment:candidates.emptyGuide")}
            </Text>
            {search.trim() ? (
              <Button type="button" variant="light" onClick={() => setSearch("")}>
                {t("recruitment:candidates.clearSearch")}
              </Button>
            ) : canManage ? (
              <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
                {t("recruitment:candidates.addCandidate")}
              </Button>
            ) : (
              <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
                {t("recruitment:candidates.emptySupport")}
              </Button>
            )}
          </Stack>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <Table striped highlightOnHover verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("recruitment:candidates.firstName")}</Table.Th>
                    <Table.Th>{t("recruitment:candidates.email")}</Table.Th>
                    <Table.Th>{t("recruitment:candidates.phone")}</Table.Th>
                    <Table.Th>{t("recruitment:candidates.source")}</Table.Th>
                    <Table.Th>{t("recruitment:candidates.tags")}</Table.Th>
                    {canManage && <Table.Th ta="right">{t("common:actions")}</Table.Th>}
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {data.map((candidate) => (
                    <Table.Tr key={candidate.id} opacity={candidate.isAnonymized ? 0.6 : 1}>
                      <Table.Td>
                        <Group gap={6} wrap="nowrap">
                          <Text fw={500}>
                            {candidate.firstName} {candidate.lastName}
                          </Text>
                          {candidate.isAnonymized && (
                            <Tooltip label={t("recruitment:candidates.anonymizedRowHint")}>
                              <Badge size="sm" color="gray" variant="light">
                                {t("recruitment:candidates.anonymized")}
                              </Badge>
                            </Tooltip>
                          )}
                        </Group>
                      </Table.Td>
                      <Table.Td>{candidate.email}</Table.Td>
                      <Table.Td>{candidate.phone ?? "—"}</Table.Td>
                      <Table.Td>
                        {enumLabel(t, "recruitment:candidates.sources", candidate.source)}
                      </Table.Td>
                      <Table.Td>
                        <Group gap={4}>
                          {candidate.tagIds.length === 0 ? (
                            <Text size="sm" c="dimmed">
                              —
                            </Text>
                          ) : (
                            candidate.tagIds.map((tagId) => (
                              <Badge key={tagId} size="sm" variant="light">
                                {tagNameById.get(tagId) ?? "…"}
                              </Badge>
                            ))
                          )}
                        </Group>
                      </Table.Td>
                      {canManage && (
                        <Table.Td>
                          <Group justify="flex-end" wrap="nowrap">
                            {candidateActions(candidate)}
                          </Group>
                        </Table.Td>
                      )}
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </div>

            {/* Dar ekran: kart listesi */}
            <div className="md:hidden p-3">
              <Stack gap="sm">
                {data.map((candidate) => (
                  <Card
                    key={candidate.id}
                    withBorder
                    padding="sm"
                    radius="md"
                    opacity={candidate.isAnonymized ? 0.6 : 1}
                  >
                    <Stack gap={6}>
                      <Group justify="space-between" align="flex-start" wrap="nowrap">
                        <Text fw={500} size="sm">
                          {candidate.firstName} {candidate.lastName}
                        </Text>
                        {candidate.isAnonymized && (
                          <Badge size="sm" color="gray" variant="light">
                            {t("recruitment:candidates.anonymized")}
                          </Badge>
                        )}
                      </Group>
                      <Text size="xs" c="dimmed">
                        {candidate.email} · {candidate.phone ?? "—"}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {enumLabel(t, "recruitment:candidates.sources", candidate.source)}
                      </Text>
                      {candidate.tagIds.length > 0 && (
                        <Group gap={4}>
                          {candidate.tagIds.map((tagId) => (
                            <Badge key={tagId} size="sm" variant="light">
                              {tagNameById.get(tagId) ?? "…"}
                            </Badge>
                          ))}
                        </Group>
                      )}
                      {canManage && (
                        <Group justify="flex-end" wrap="nowrap">
                          {candidateActions(candidate)}
                        </Group>
                      )}
                    </Stack>
                  </Card>
                ))}
              </Stack>
            </div>
          </>
        )}
      </Card>

      <Modal
        opened={dialog.open}
        onClose={() => setDialog((prev) => ({ ...prev, open: false }))}
        title={
          dialog.candidate
            ? t("recruitment:candidates.dialogEditTitle")
            : t("recruitment:candidates.dialogCreateTitle")
        }
      >
        <form onSubmit={handleSubmit(onSubmit)}>
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
              <Button
                type="button"
                variant="default"
                onClick={() => setDialog((prev) => ({ ...prev, open: false }))}
                disabled={isSaving}
              >
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={isSaving}>
                {isSaving ? t("common:saving") : t("common:save")}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>

      <Modal
        opened={tagCandidate !== null}
        onClose={() => setTagCandidate(null)}
        title={t("recruitment:candidates.editTags")}
      >
        <Stack gap="md">
          <MultiSelect
            label={t("recruitment:candidates.tags")}
            data={(tagsQuery.data ?? [])
              .filter((tag) => tag.isActive)
              .map((tag) => ({ value: tag.id, label: tag.name }))}
            value={selectedTagIds}
            onChange={setSelectedTagIds}
            searchable
          />
          <Group gap="xs" align="flex-end">
            <TextInput
              className="flex-1"
              label={t("recruitment:candidates.newTag")}
              value={newTagName}
              onChange={(e) => setNewTagName(e.currentTarget.value)}
            />
            <Button
              type="button"
              variant="default"
              disabled={!newTagName.trim() || createTagMutation.isPending}
              onClick={() => createTagMutation.mutate(newTagName.trim())}
            >
              {t("recruitment:candidates.addTag")}
            </Button>
          </Group>
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              onClick={() => setTagCandidate(null)}
              disabled={setTagsMutation.isPending}
            >
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              disabled={setTagsMutation.isPending}
              onClick={() =>
                tagCandidate &&
                setTagsMutation.mutate({ id: tagCandidate.id, tagIds: selectedTagIds })
              }
            >
              {setTagsMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={anonymizeTarget !== null}
        onClose={() => setAnonymizeTarget(null)}
        title={t("recruitment:candidates.anonymize")}
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("recruitment:candidates.anonymizeDescription")}
          </Text>
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              onClick={() => setAnonymizeTarget(null)}
              disabled={anonymizeMutation.isPending}
            >
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              color="red"
              loading={anonymizeMutation.isPending}
              onClick={() => anonymizeTarget && anonymizeMutation.mutate(anonymizeTarget.id)}
            >
              {t("recruitment:candidates.anonymize")}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={anonymizeExpiredOpen}
        onClose={() => setAnonymizeExpiredOpen(false)}
        title={t("recruitment:candidates.anonymizeExpired.dialogTitle")}
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("recruitment:candidates.anonymizeExpired.description")}
          </Text>
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              onClick={() => setAnonymizeExpiredOpen(false)}
              disabled={anonymizeExpiredMutation.isPending}
            >
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              color="red"
              loading={anonymizeExpiredMutation.isPending}
              onClick={() => anonymizeExpiredMutation.mutate()}
            >
              {t("recruitment:candidates.anonymizeExpired.confirm")}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Drawer
        opened={applicationsCandidate !== null}
        onClose={() => setApplicationsCandidate(null)}
        title={
          applicationsCandidate
            ? t("recruitment:candidates.applications.drawerTitle", {
                name: `${applicationsCandidate.firstName} ${applicationsCandidate.lastName}`,
              })
            : ""
        }
        position="right"
      >
        <Stack gap="sm">
          {applicationsQuery.isLoading ? (
            <Text size="sm" c="dimmed">
              {t("common:loading")}
            </Text>
          ) : applicationsQuery.isError ? (
            <Text size="sm" c="red">
              {getApiErrorMessage(applicationsQuery.error, t("recruitment:genericError"))}
            </Text>
          ) : !applicationsQuery.data || applicationsQuery.data.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t("recruitment:candidates.applications.empty")}
            </Text>
          ) : (
            applicationsQuery.data.map((application) => (
              <Card key={application.id} withBorder padding="sm">
                <Stack gap={4}>
                  <Group justify="space-between" wrap="nowrap">
                    <Text size="sm" fw={500}>
                      {postingTitleById.get(application.jobPostingId) ??
                        application.jobPostingId.slice(0, 8)}
                    </Text>
                    <Badge size="sm" variant="light">
                      {t(`recruitment:applications.stages.${application.stage}`)}
                    </Badge>
                  </Group>
                  <Text size="xs" c="dimmed">
                    {t("recruitment:applications.appliedAt")}:{" "}
                    {formatDateTime(application.appliedAt, i18n.language)}
                  </Text>
                </Stack>
              </Card>
            ))
          )}
        </Stack>
      </Drawer>
    </div>
  );
}
