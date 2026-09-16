import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Info, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  Modal,
  Paper,
  ScrollArea,
  Skeleton,
  Stack,
  Switch,
  Table,
  Text,
  Tooltip,
} from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import ConfirmDialog from "@/components/confirm-dialog";
import SearchPicker, { type PickerOption } from "@/components/structure/search-picker";
import { structureErrorMessage } from "@/components/structure/errors";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import {
  grantUnitAuthorization,
  listUnitAuthorizations,
  revokeUnitAuthorization,
  searchUnitAuthorizationCandidates,
  type OrgUnit,
  type UnitAuthorization,
} from "@/services/structure.service";

interface UnitAuthorizationDialogProps {
  unit: OrgUnit;
  onClose: () => void;
}

interface EditorState {
  person: PickerOption | null;
  canViewSalaryAndPayments: boolean;
  canViewUnitReports: boolean;
  /** Set when editing an existing grant (person is fixed). */
  existing?: UnitAuthorization;
}

/** Label on the left, switch pushed to the right edge (settings-row layout). */
const SWITCH_STYLES = {
  body: { justifyContent: "space-between", gap: "var(--mantine-spacing-md)" },
  labelWrapper: { flex: 1 },
} as const;

const personLabel =(grant: UnitAuthorization) => grant.userFullName || grant.userEmail || grant.userId;

/** "Birim yetkilendirme": grants a person access to a unit and all of its sub-units. */
export default function UnitAuthorizationDialog({ unit, onClose }: UnitAuthorizationDialogProps) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("identity.unit_authorizations.manage");
  const isMobile = useMediaQuery("(max-width: 390px)");

  const queryKey = ["identity", "unit-authorizations", unit.id];
  const { data, isLoading, isError } = useQuery({
    queryKey,
    queryFn: () => listUnitAuthorizations(unit.id),
  });
  const grants = (data ?? []).filter((g) => g.isActive);

  const [editor, setEditor] = useState<EditorState | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<UnitAuthorization | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey });

  const saveMutation = useMutation({
    mutationFn: (state: EditorState) =>
      grantUnitAuthorization({
        orgUnitId: unit.id,
        userId: state.existing?.userId ?? state.person!.id,
        canViewSalaryAndPayments: state.canViewSalaryAndPayments,
        canViewUnitReports: state.canViewUnitReports,
      }),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:authorization.saveSuccess") });
      setEditor(null);
      setSubmitted(false);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const removeMutation = useMutation({
    mutationFn: revokeUnitAuthorization,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:authorization.removeSuccess") });
      setRemoveTarget(null);
    },
    onError: (error) => {
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) });
      setRemoveTarget(null);
    },
  });

  const personMissing = editor !== null && !editor.existing && !editor.person;
  const flagMissing = editor !== null && !editor.canViewSalaryAndPayments && !editor.canViewUnitReports;

  function submit() {
    setSubmitted(true);
    if (!editor || personMissing || flagMissing) return;
    saveMutation.mutate(editor);
  }

  return (
    <>
      <Modal
        opened
        size="xl"
        fullScreen={isMobile}
        onClose={() => !saveMutation.isPending && onClose()}
        title={
          <Group gap="xs" wrap="nowrap">
            <ShieldCheck size={20} />
            <Text fw={600}>{t("structure:authorization.title")}</Text>
          </Group>
        }
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("structure:authorization.subtitle", { name: unit.name })}
          </Text>

          <Alert variant="light" icon={<Info size={16} />}>
            <Stack gap={4}>
              <Text size="xs">{t("structure:authorization.explainScope")}</Text>
              <Text size="xs">{t("structure:authorization.explainAbilities")}</Text>
              <Text size="xs">{t("structure:authorization.explainCustomReports")}</Text>
            </Stack>
          </Alert>

          {isLoading ? (
            <Stack gap="xs">
              <Skeleton h={32} />
              <Skeleton h={32} />
            </Stack>
          ) : isError ? (
            <Text size="sm" c="red">
              {t("structure:errors.generic")}
            </Text>
          ) : grants.length === 0 ? (
            <Paper withBorder p="md" style={{ borderStyle: "dashed" }}>
              <Text size="sm" c="dimmed" ta="center">
                {t("structure:authorization.empty")}
              </Text>
            </Paper>
          ) : (
            <>
              {/* Geniş ekran: tablo */}
              <Paper withBorder className="hidden md:block">
                <ScrollArea.Autosize mah={288}>
                  <Table>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t("structure:authorization.person")}</Table.Th>
                        <Table.Th>{t("structure:authorization.permissions")}</Table.Th>
                        {canManage && (
                          <Table.Th w={96} ta="right">
                            {t("common:actions")}
                          </Table.Th>
                        )}
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {grants.map((grant) => (
                        <Table.Tr key={grant.id}>
                          <Table.Td>
                            <Text size="sm" fw={500}>
                              {personLabel(grant)}
                            </Text>
                            {grant.userEmail && grant.userFullName && (
                              <Text size="xs" c="dimmed">
                                {grant.userEmail}
                              </Text>
                            )}
                          </Table.Td>
                          <Table.Td>
                            <Group gap={4}>
                              <Badge variant="outline">{t("structure:authorization.base")}</Badge>
                              {grant.canViewSalaryAndPayments && (
                                <Badge variant="light">{t("structure:authorization.salaryShort")}</Badge>
                              )}
                              {grant.canViewUnitReports && (
                                <Badge variant="light">{t("structure:authorization.reportsShort")}</Badge>
                              )}
                            </Group>
                          </Table.Td>
                          {canManage && (
                            <Table.Td>
                              <Group gap={4} justify="flex-end" wrap="nowrap">
                                <Tooltip label={t("common:edit")}>
                                  <ActionIcon
                                    variant="subtle"
                                    color="gray"
                                    aria-label={t("common:edit")}
                                    onClick={() => {
                                      setSubmitted(false);
                                      setEditor({
                                        person: { id: grant.userId, label: personLabel(grant) },
                                        canViewSalaryAndPayments: grant.canViewSalaryAndPayments,
                                        canViewUnitReports: grant.canViewUnitReports,
                                        existing: grant,
                                      });
                                    }}
                                  >
                                    <Pencil size={16} />
                                  </ActionIcon>
                                </Tooltip>
                                <Tooltip label={t("structure:authorization.remove")}>
                                  <ActionIcon
                                    variant="subtle"
                                    color="red"
                                    aria-label={t("structure:authorization.remove")}
                                    onClick={() => setRemoveTarget(grant)}
                                  >
                                    <Trash2 size={16} />
                                  </ActionIcon>
                                </Tooltip>
                              </Group>
                            </Table.Td>
                          )}
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea.Autosize>
              </Paper>

              {/* Dar ekran (≤390px dahil): kart listesi */}
              <Stack gap="xs" className="md:hidden">
                {grants.map((grant) => (
                  <Paper key={grant.id} withBorder p="sm" radius="md">
                    <Group justify="space-between" wrap="nowrap" align="flex-start">
                      <Stack gap={2} className="min-w-0">
                        <Text size="sm" fw={500} truncate>
                          {personLabel(grant)}
                        </Text>
                        {grant.userEmail && grant.userFullName && (
                          <Text size="xs" c="dimmed" truncate>
                            {grant.userEmail}
                          </Text>
                        )}
                      </Stack>
                      {canManage && (
                        <Group gap={4} wrap="nowrap">
                          <ActionIcon
                            variant="subtle"
                            color="gray"
                            size="lg"
                            aria-label={t("common:edit")}
                            onClick={() => {
                              setSubmitted(false);
                              setEditor({
                                person: { id: grant.userId, label: personLabel(grant) },
                                canViewSalaryAndPayments: grant.canViewSalaryAndPayments,
                                canViewUnitReports: grant.canViewUnitReports,
                                existing: grant,
                              });
                            }}
                          >
                            <Pencil size={18} />
                          </ActionIcon>
                          <ActionIcon
                            variant="subtle"
                            color="red"
                            size="lg"
                            aria-label={t("structure:authorization.remove")}
                            onClick={() => setRemoveTarget(grant)}
                          >
                            <Trash2 size={18} />
                          </ActionIcon>
                        </Group>
                      )}
                    </Group>
                    <Group gap={4} mt="xs" wrap="wrap">
                      <Badge variant="outline">{t("structure:authorization.base")}</Badge>
                      {grant.canViewSalaryAndPayments && (
                        <Badge variant="light">{t("structure:authorization.salaryShort")}</Badge>
                      )}
                      {grant.canViewUnitReports && (
                        <Badge variant="light">{t("structure:authorization.reportsShort")}</Badge>
                      )}
                    </Group>
                  </Paper>
                ))}
              </Stack>
            </>
          )}

          {canManage && editor && (
            <Paper withBorder p="md" bg="var(--mantine-color-default-hover)">
              <Stack gap="md">
                <SearchPicker
                  id="auth-person"
                  label={t("structure:authorization.person")}
                  error={submitted && personMissing ? t("common:required") : undefined}
                  queryKey={["identity", "unit-authorization-candidates"]}
                  search={async (q) =>
                    (await searchUnitAuthorizationCandidates(q)).map((c) => ({
                      id: c.userId,
                      label: c.fullName,
                      description: c.email,
                    }))
                  }
                  value={editor.person}
                  onChange={(person) => setEditor((prev) => (prev ? { ...prev, person } : prev))}
                  placeholder={t("structure:authorization.selectPerson")}
                  disabled={!!editor.existing}
                  allowClear={!editor.existing}
                />
                <Switch
                  id="auth-salary"
                  labelPosition="left"
                  label={t("structure:authorization.canViewSalary")}
                  styles={SWITCH_STYLES}
                  checked={editor.canViewSalaryAndPayments}
                  onChange={(e) => {
                    const checked = e.currentTarget.checked;
                    setEditor((prev) => (prev ? { ...prev, canViewSalaryAndPayments: checked } : prev));
                  }}
                />
                <Switch
                  id="auth-reports"
                  labelPosition="left"
                  label={t("structure:authorization.canViewReports")}
                  styles={SWITCH_STYLES}
                  checked={editor.canViewUnitReports}
                  onChange={(e) => {
                    const checked = e.currentTarget.checked;
                    setEditor((prev) => (prev ? { ...prev, canViewUnitReports: checked } : prev));
                  }}
                />
                {submitted && flagMissing && (
                  <Text size="xs" c="red">
                    {t("structure:authorization.flagRequired")}
                  </Text>
                )}
                <Group justify="flex-end" gap="sm">
                  <Button
                    type="button"
                    variant="default"
                    onClick={() => setEditor(null)}
                    disabled={saveMutation.isPending}
                  >
                    {t("common:cancel")}
                  </Button>
                  <Button type="button" onClick={submit} disabled={saveMutation.isPending}>
                    {saveMutation.isPending ? t("common:saving") : t("common:save")}
                  </Button>
                </Group>
              </Stack>
            </Paper>
          )}

          <Group justify="space-between">
            {canManage && !editor ? (
              <Button
                type="button"
                variant="default"
                leftSection={<Plus size={16} />}
                onClick={() => {
                  setSubmitted(false);
                  setEditor({ person: null, canViewSalaryAndPayments: false, canViewUnitReports: true });
                }}
              >
                {t("structure:authorization.add")}
              </Button>
            ) : (
              <span />
            )}
            <Button type="button" variant="subtle" onClick={onClose}>
              {t("common:close")}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <ConfirmDialog
        open={removeTarget !== null}
        onOpenChange={(open) => !open && setRemoveTarget(null)}
        title={t("structure:authorization.removeTitle")}
        description={t("structure:authorization.removeConfirm", {
          person: removeTarget ? personLabel(removeTarget) : "",
          unit: unit.name,
        })}
        confirmLabel={t("structure:authorization.remove")}
        onConfirm={() => removeTarget && removeMutation.mutate(removeTarget.id)}
        isLoading={removeMutation.isPending}
      />
    </>
  );
}
