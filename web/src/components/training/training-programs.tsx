import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { Badge, Button, Card, Group, Skeleton, Stack, Table, Text } from "@mantine/core";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import TrainingProgramDetail from "@/components/training/training-program-detail";
import TrainingProgramFormDialog from "@/components/training/training-program-form-dialog";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDate } from "@/lib/dates";
import {
  activateTrainingProgram,
  closeTrainingProgram,
  enumKey,
  listTrainingPrograms,
  type TrainingProgram,
  type TrainingStatus,
} from "@/services/training.service";

const PROGRAMS_QUERY_KEY = ["training", "programs"] as const;

export const PROGRAM_STATUS_COLOR: Record<TrainingStatus, string> = {
  draft: "gray",
  active: "green",
  closed: "dark",
  archived: "dark",
};

/** İK eğitim programları (training.programs.view): liste, oluştur, etkinleştir, kapat, detay (katılımcı/ön koşul). */
export default function TrainingPrograms() {
  const { t, i18n } = useTranslation(["training", "common"]);
  const canManage = usePermission("training.programs.manage");
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [detail, setDetail] = useState<TrainingProgram | null>(null);
  const [closeTarget, setCloseTarget] = useState<TrainingProgram | null>(null);

  const programsQuery = useQuery({ queryKey: PROGRAMS_QUERY_KEY, queryFn: listTrainingPrograms });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: PROGRAMS_QUERY_KEY });

  const onError = (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("training:actionFailed")),
    });
  const activateMutation = useMutation({
    mutationFn: (id: string) => activateTrainingProgram(id),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("training:programs.activated"),
      });
      refresh();
    },
    onError,
  });
  const closeMutation = useMutation({
    mutationFn: (id: string) => closeTrainingProgram(id),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("training:programs.closed"),
      });
      setCloseTarget(null);
      refresh();
    },
    onError,
  });

  const programs = [...(programsQuery.data ?? [])].sort((a, b) =>
    b.startDate.localeCompare(a.startDate)
  );

  return (
    <Stack gap="md">
      {canManage && (
        <Group justify="flex-end">
          <Button leftSection={<Plus size={16} />} onClick={() => setCreateOpen(true)}>
            {t("training:programs.create")}
          </Button>
        </Group>
      )}

      {programsQuery.isLoading && <Skeleton height={160} />}
      {programsQuery.isError && <Text c="red">{t("training:loadError")}</Text>}
      {programsQuery.isSuccess && programs.length === 0 && (
        <Card withBorder>
          <Text c="dimmed">{t("training:programs.empty")}</Text>
        </Card>
      )}
      {programs.length > 0 && (
        <Table.ScrollContainer minWidth={760}>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("training:fields.title")}</Table.Th>
                <Table.Th>{t("training:fields.category")}</Table.Th>
                <Table.Th>{t("training:fields.dates")}</Table.Th>
                <Table.Th>{t("training:fields.capacity")}</Table.Th>
                <Table.Th>{t("training:fields.status")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {programs.map((program) => {
                const status = enumKey<TrainingStatus>(program.status);
                const dates = [program.startDate, program.endDate]
                  .filter((value): value is string => Boolean(value))
                  .map((value) => formatDate(value, i18n.language))
                  .join(" – ");
                return (
                  <Table.Tr key={program.id}>
                    <Table.Td>{program.title}</Table.Td>
                    <Table.Td>{t(`training:categories.${enumKey(program.category)}`)}</Table.Td>
                    <Table.Td>{dates}</Table.Td>
                    <Table.Td>{program.maxCapacity ?? "—"}</Table.Td>
                    <Table.Td>
                      <Badge color={PROGRAM_STATUS_COLOR[status] ?? "gray"} variant="light">
                        {t(`training:programStatuses.${status}`)}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="flex-end" wrap="nowrap">
                        <Button
                          size="compact-sm"
                          variant="light"
                          onClick={() => setDetail(program)}
                        >
                          {t("training:programs.details")}
                        </Button>
                        {canManage && status === "draft" && (
                          <Button
                            size="compact-sm"
                            variant="subtle"
                            loading={
                              activateMutation.isPending &&
                              activateMutation.variables === program.id
                            }
                            onClick={() => activateMutation.mutate(program.id)}
                          >
                            {t("training:programs.activate")}
                          </Button>
                        )}
                        {canManage && (status === "draft" || status === "active") && (
                          <Button
                            size="compact-sm"
                            variant="subtle"
                            color="red"
                            onClick={() => setCloseTarget(program)}
                          >
                            {t("training:programs.close")}
                          </Button>
                        )}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      {createOpen && (
        <TrainingProgramFormDialog onClose={() => setCreateOpen(false)} onCreated={refresh} />
      )}
      {detail && (
        <TrainingProgramDetail
          program={detail}
          programs={programs}
          onClose={() => setDetail(null)}
        />
      )}
      <ConfirmDialog
        open={closeTarget !== null}
        onOpenChange={(open) => !open && setCloseTarget(null)}
        title={t("training:programs.closeTitle")}
        description={t("training:programs.closeDescription", { title: closeTarget?.title ?? "" })}
        confirmLabel={t("training:programs.close")}
        onConfirm={() => closeTarget && closeMutation.mutate(closeTarget.id)}
        isLoading={closeMutation.isPending}
      />
    </Stack>
  );
}
