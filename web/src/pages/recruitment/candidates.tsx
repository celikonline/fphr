import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Search, UserRound } from "lucide-react";
import { ActionIcon, Button, Card, Group, Modal, Stack, Table, Text, TextInput, Title, Tooltip } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { createCandidate, listCandidates, updateCandidate, type Candidate } from "@/services/recruitment.service";

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
  const { t } = useTranslation(["recruitment", "common"]);
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

  const [dialog, setDialog] = useState<DialogState>({ open: false });
  const { register, handleSubmit, reset, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: "", lastName: "", email: "", phone: "" },
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: CANDIDATES_KEY });

  const createMutation = useMutation({
    mutationFn: createCandidate,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:candidates.createSuccess") });
      setDialog({ open: false });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...input }: { id: string } & FormValues) => updateCandidate(id, input),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:candidates.updateSuccess") });
      setDialog({ open: false });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  function openCreate() {
    reset({ firstName: "", lastName: "", email: "", phone: "" });
    setDialog({ open: true });
  }

  function openEdit(candidate: Candidate) {
    reset({ firstName: candidate.firstName, lastName: candidate.lastName, email: candidate.email, phone: candidate.phone ?? "" });
    setDialog({ open: true, candidate });
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
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <UserRound size={24} /> {t("recruitment:candidates.title")}
          </Title>
          <Text c="dimmed">{t("recruitment:candidates.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("recruitment:candidates.addCandidate")}
          </Button>
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
          <Text p="md" size="sm" c="dimmed">
            {t("recruitment:candidates.empty")}
          </Text>
        ) : (
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("recruitment:candidates.firstName")}</Table.Th>
                <Table.Th>{t("recruitment:candidates.email")}</Table.Th>
                <Table.Th>{t("recruitment:candidates.phone")}</Table.Th>
                <Table.Th>{t("recruitment:candidates.source")}</Table.Th>
                {canManage && <Table.Th ta="right">{t("common:actions")}</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((candidate) => (
                <Table.Tr key={candidate.id}>
                  <Table.Td>
                    <Text fw={500}>
                      {candidate.firstName} {candidate.lastName}
                    </Text>
                  </Table.Td>
                  <Table.Td>{candidate.email}</Table.Td>
                  <Table.Td>{candidate.phone ?? "—"}</Table.Td>
                  <Table.Td>{t(`recruitment:candidates.sources.${candidate.source}`)}</Table.Td>
                  {canManage && (
                    <Table.Td>
                      <Group justify="flex-end">
                        <Tooltip label={t("recruitment:candidates.editCandidate")}>
                          <ActionIcon variant="subtle" color="gray" onClick={() => openEdit(candidate)}>
                            <Pencil size={16} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal
        opened={dialog.open}
        onClose={() => setDialog((prev) => ({ ...prev, open: false }))}
        title={dialog.candidate ? t("recruitment:candidates.dialogEditTitle") : t("recruitment:candidates.dialogCreateTitle")}
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
              <Button type="button" variant="default" onClick={() => setDialog((prev) => ({ ...prev, open: false }))} disabled={isSaving}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={isSaving}>
                {isSaving ? t("common:saving") : t("common:save")}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </div>
  );
}
