import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Divider,
  Group,
  Modal,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import EmployeeNameLink from "@/components/employees/employee-name-link";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  cancelEnrollment,
  completeEnrollment,
  createTrainingRequirement,
  deleteTrainingRequirement,
  enrollEmployee,
  enumKey,
  listEnrollments,
  listTrainingRequirements,
  type Enrollment,
  type EnrollmentStatus,
  type TrainingProgram,
  type TrainingStatus,
} from "@/services/training.service";

export const ENROLLMENT_STATUS_COLOR: Record<EnrollmentStatus, string> = {
  enrolled: "blue",
  completed: "green",
  cancelled: "gray",
  noShow: "orange",
};

function useNotify() {
  const { t } = useTranslation(["training", "common"]);
  return {
    ok: (description: string) => toast({ variant: "success", title: t("common:success"), description }),
    fail: (error: unknown) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("training:actionFailed")),
      }),
  };
}

interface TrainingProgramDetailProps {
  program: TrainingProgram;
  /** Every program, to pick prerequisites from. */
  programs: TrainingProgram[];
  onClose: () => void;
}

/** Program detayı: katılımcılar (ekle / tamamla / çıkar) ve ön koşul eğitimler. */
export default function TrainingProgramDetail({ program, programs, onClose }: TrainingProgramDetailProps) {
  const { t } = useTranslation(["training"]);
  const canViewEnrollments = usePermission("training.enrollments.view");

  return (
    <Modal opened onClose={onClose} title={program.title} centered size="xl">
      <Stack gap="lg">
        {program.description && (
          <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
            {program.description}
          </Text>
        )}
        {canViewEnrollments && <Participants program={program} />}
        <Divider />
        <Title order={5}>{t("training:requirements.title")}</Title>
        <Requirements program={program} programs={programs} />
      </Stack>
    </Modal>
  );
}

function Participants({ program }: { program: TrainingProgram }) {
  const { t, i18n } = useTranslation(["training", "common"]);
  const canManage = usePermission("training.enrollments.manage");
  const queryClient = useQueryClient();
  const notify = useNotify();
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [completing, setCompleting] = useState<Enrollment | null>(null);
  const [certificate, setCertificate] = useState("");
  const queryKey = ["training", "enrollments", program.id];
  const enrollmentsQuery = useQuery({ queryKey, queryFn: () => listEnrollments(program.id) });
  const refresh = () => void queryClient.invalidateQueries({ queryKey });

  const programStatus = enumKey<TrainingStatus>(program.status);
  const isOpen = programStatus === "draft" || programStatus === "active";
  const enrollments = enrollmentsQuery.data ?? [];
  const isFull = program.maxCapacity != null && enrollments.length >= program.maxCapacity;

  const enrollMutation = useMutation({
    mutationFn: (employeeId: string) => enrollEmployee(program.id, employeeId),
    onSuccess: () => {
      notify.ok(t("training:participants.added"));
      setEmployee(null);
      refresh();
    },
    onError: notify.fail,
  });
  const completeMutation = useMutation({
    mutationFn: ({ id, cert }: { id: string; cert: string }) => completeEnrollment(id, cert.trim() || null),
    onSuccess: () => {
      notify.ok(t("training:participants.completed"));
      setCompleting(null);
      refresh();
    },
    onError: notify.fail,
  });
  const removeMutation = useMutation({
    mutationFn: (id: string) => cancelEnrollment(id),
    onSuccess: () => {
      notify.ok(t("training:participants.removed"));
      refresh();
    },
    onError: notify.fail,
  });

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Title order={5}>{t("training:participants.title")}</Title>
        {program.maxCapacity != null && (
          <Text size="sm" c={isFull ? "red" : "dimmed"}>
            {t("training:participants.capacity", { count: enrollments.length, max: program.maxCapacity })}
          </Text>
        )}
      </Group>

      {canManage && isOpen && (
        <Group align="flex-end" gap="sm">
          <div style={{ flex: 1 }}>
            <EmployeePicker
              id="training-enroll-employee"
              label={t("training:participants.add")}
              value={employee}
              onChange={setEmployee}
              disabled={isFull}
            />
          </div>
          <Button
            onClick={() => employee && enrollMutation.mutate(employee.id)}
            disabled={!employee || isFull}
            loading={enrollMutation.isPending}
          >
            {t("training:participants.addButton")}
          </Button>
        </Group>
      )}
      {canManage && !isOpen && (
        <Text size="xs" c="dimmed">
          {t("training:participants.closedHint")}
        </Text>
      )}

      {enrollmentsQuery.isLoading && <Skeleton height={80} />}
      {enrollmentsQuery.isError && <Text c="red">{t("training:loadError")}</Text>}
      {enrollmentsQuery.isSuccess && enrollments.length === 0 && (
        <Text size="sm" c="dimmed">
          {t("training:participants.empty")}
        </Text>
      )}
      {enrollments.length > 0 && (
        <Table.ScrollContainer minWidth={560}>
          <Table>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("training:participants.employee")}</Table.Th>
                <Table.Th>{t("training:fields.status")}</Table.Th>
                <Table.Th>{t("training:participants.certificate")}</Table.Th>
                {canManage && <Table.Th />}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {enrollments.map((enrollment) => {
                const status = enumKey<EnrollmentStatus>(enrollment.status);
                return (
                  <Table.Tr key={enrollment.id}>
                    <Table.Td>
                      <EmployeeNameLink id={enrollment.employeeId} />
                    </Table.Td>
                    <Table.Td>
                      <Badge color={ENROLLMENT_STATUS_COLOR[status] ?? "gray"} variant="light">
                        {t(`training:enrollmentStatuses.${status}`)}
                      </Badge>
                      {enrollment.completedAt && (
                        <Text size="xs" c="dimmed">
                          {formatDateTime(enrollment.completedAt, i18n.language)}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>{enrollment.certificateNumber ?? "—"}</Table.Td>
                    {canManage && (
                      <Table.Td>
                        <Group gap="xs" justify="flex-end" wrap="nowrap">
                          {status === "enrolled" && (
                            <Button
                              size="compact-sm"
                              variant="light"
                              onClick={() => {
                                setCertificate("");
                                setCompleting(enrollment);
                              }}
                            >
                              {t("training:participants.complete")}
                            </Button>
                          )}
                          {status !== "completed" && (
                            <Button
                              size="compact-sm"
                              variant="subtle"
                              color="red"
                              loading={removeMutation.isPending && removeMutation.variables === enrollment.id}
                              onClick={() => removeMutation.mutate(enrollment.id)}
                            >
                              {t("training:participants.remove")}
                            </Button>
                          )}
                        </Group>
                      </Table.Td>
                    )}
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Modal
        opened={completing !== null}
        onClose={() => setCompleting(null)}
        title={t("training:participants.completeTitle")}
        centered
      >
        <Stack gap="md">
          <TextInput
            id="training-certificate"
            label={t("training:participants.certificate")}
            description={t("training:participants.certificateHint")}
            value={certificate}
            onChange={(e) => setCertificate(e.currentTarget.value)}
          />
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={() => setCompleting(null)}>
              {t("common:cancel")}
            </Button>
            <Button
              loading={completeMutation.isPending}
              onClick={() => completing && completeMutation.mutate({ id: completing.id, cert: certificate })}
            >
              {t("training:participants.complete")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}

function Requirements({ program, programs }: { program: TrainingProgram; programs: TrainingProgram[] }) {
  const { t } = useTranslation(["training"]);
  const canManage = usePermission("training.programs.manage");
  const queryClient = useQueryClient();
  const notify = useNotify();
  const [prerequisiteId, setPrerequisiteId] = useState<string | null>(null);
  const [blocks, setBlocks] = useState(true);
  const queryKey = ["training", "requirements", program.id];
  const requirementsQuery = useQuery({ queryKey, queryFn: () => listTrainingRequirements(program.id) });
  const refresh = () => void queryClient.invalidateQueries({ queryKey });

  const requirements = requirementsQuery.data ?? [];
  const titleOf = (id: string) => programs.find((p) => p.id === id)?.title ?? "—";
  const candidates = programs.filter(
    (p) => p.id !== program.id && !requirements.some((r) => r.prerequisiteTrainingProgramId === p.id)
  );

  const addMutation = useMutation({
    mutationFn: () => createTrainingRequirement(program.id, prerequisiteId!, blocks),
    onSuccess: () => {
      notify.ok(t("training:requirements.added"));
      setPrerequisiteId(null);
      refresh();
    },
    onError: notify.fail,
  });
  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteTrainingRequirement(id),
    onSuccess: refresh,
    onError: notify.fail,
  });

  return (
    <Stack gap="sm">
      {requirementsQuery.isSuccess && requirements.length === 0 && (
        <Text size="sm" c="dimmed">
          {t("training:requirements.empty")}
        </Text>
      )}
      {requirements.map((requirement) => (
        <Group key={requirement.id} justify="space-between" wrap="nowrap">
          <Group gap="xs">
            <Text size="sm">{titleOf(requirement.prerequisiteTrainingProgramId)}</Text>
            {requirement.blocksAssignmentUntilCompleted && (
              <Badge size="sm" color="orange" variant="light">
                {t("training:requirements.blocking")}
              </Badge>
            )}
          </Group>
          {canManage && (
            <ActionIcon
              variant="subtle"
              color="red"
              aria-label={t("training:requirements.remove")}
              onClick={() => deleteMutation.mutate(requirement.id)}
            >
              <Trash2 size={16} />
            </ActionIcon>
          )}
        </Group>
      ))}
      {canManage && candidates.length > 0 && (
        <Group align="flex-end" gap="sm">
          <Select
            id="training-prerequisite"
            label={t("training:requirements.add")}
            data={candidates.map((p) => ({ value: p.id, label: p.title }))}
            value={prerequisiteId}
            onChange={setPrerequisiteId}
            searchable
            style={{ flex: 1 }}
          />
          <Checkbox
            label={t("training:requirements.blocksLabel")}
            checked={blocks}
            onChange={(e) => setBlocks(e.currentTarget.checked)}
            mb={8}
          />
          <Button onClick={() => addMutation.mutate()} disabled={!prerequisiteId} loading={addMutation.isPending}>
            {t("training:requirements.addButton")}
          </Button>
        </Group>
      )}
    </Stack>
  );
}
