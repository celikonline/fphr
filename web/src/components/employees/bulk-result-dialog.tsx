import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Badge, type BadgeProps, Button, Card, Group, Modal, Paper, SimpleGrid, Stack, Table, Text } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { CheckCircle2, CircleSlash, XCircle } from "lucide-react";
import type { BulkItemOutcome, BulkOperationResult } from "@/types";
import { bulkCodeKey } from "./bulk-actions";

interface BulkResultDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  result: BulkOperationResult | null;
  /** Names of employees seen in the grid; ids of unseen ones (all-matching mode) are shown shortened. */
  nameById: ReadonlyMap<string, string>;
}

const OUTCOME_BADGE: Record<BulkItemOutcome, Pick<BadgeProps, "variant" | "color">> = {
  succeeded: { variant: "filled", color: "green" },
  skipped: { variant: "light" },
  failed: { variant: "filled", color: "red" },
};

export default function BulkResultDialog({ open, onOpenChange, title, result, nameById }: BulkResultDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const problems = (result?.items ?? []).filter((item) => item.outcome !== "succeeded");
  const isMobile = useMediaQuery("(max-width: 390px)");

  return (
    <Modal opened={open} onClose={() => onOpenChange(false)} title={title} size="lg" fullScreen={isMobile}>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t("employees:bulk.result.requested", { count: result?.requested ?? 0 })}
        </Text>

        {result && (
          <>
            <SimpleGrid cols={3} spacing="sm">
              <Summary
                icon={<CheckCircle2 size={20} color="var(--mantine-color-green-6)" />}
                label={t("employees:bulk.outcome.succeeded")}
                value={result.succeeded}
              />
              <Summary
                icon={<CircleSlash size={20} color="var(--mantine-color-dimmed)" />}
                label={t("employees:bulk.outcome.skipped")}
                value={result.skipped}
              />
              <Summary
                icon={<XCircle size={20} color="var(--mantine-color-red-6)" />}
                label={t("employees:bulk.outcome.failed")}
                value={result.failed}
              />
            </SimpleGrid>

            {problems.length > 0 && (
              <Paper withBorder>
                {/* Geniş ekran: tablo */}
                <div className="hidden md:block">
                  <Table.ScrollContainer minWidth={420}>
                    <Table>
                      <Table.Thead>
                        <Table.Tr>
                          <Table.Th>{t("employees:columns.name")}</Table.Th>
                          <Table.Th>{t("employees:bulk.result.outcome")}</Table.Th>
                          <Table.Th>{t("employees:bulk.result.reason")}</Table.Th>
                        </Table.Tr>
                      </Table.Thead>
                      <Table.Tbody>
                        {problems.map((item) => (
                          <Table.Tr key={item.employeeId}>
                            <Table.Td fw={500}>{nameById.get(item.employeeId) ?? `#${item.employeeId.slice(0, 8)}`}</Table.Td>
                            <Table.Td>
                              <Badge {...OUTCOME_BADGE[item.outcome]}>
                                {t(`employees:bulk.outcome.${item.outcome}`)}
                              </Badge>
                            </Table.Td>
                            <Table.Td fz="sm">{item.code ? t(bulkCodeKey(item.code), { defaultValue: item.code }) : "—"}</Table.Td>
                          </Table.Tr>
                        ))}
                      </Table.Tbody>
                    </Table>
                  </Table.ScrollContainer>
                </div>

                {/* Dar ekran (≤390px dahil): kart listesi */}
                <div className="md:hidden">
                  <Stack gap="sm" p="sm">
                    {problems.map((item) => (
                      <Card key={item.employeeId} withBorder padding="sm" radius="md">
                        <Group justify="space-between" align="flex-start" wrap="nowrap">
                          <Text fw={500} size="sm">
                            {nameById.get(item.employeeId) ?? `#${item.employeeId.slice(0, 8)}`}
                          </Text>
                          <Badge {...OUTCOME_BADGE[item.outcome]}>{t(`employees:bulk.outcome.${item.outcome}`)}</Badge>
                        </Group>
                        {item.code && (
                          <Text size="sm" c="red" mt={4}>
                            {t(bulkCodeKey(item.code), { defaultValue: item.code })}
                          </Text>
                        )}
                      </Card>
                    ))}
                  </Stack>
                </div>
              </Paper>
            )}
          </>
        )}

        <Group justify="flex-end">
          <Button type="button" onClick={() => onOpenChange(false)}>
            {t("common:close")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function Summary({ icon, label, value }: { icon: ReactNode; label: string; value: number }) {
  return (
    <Paper withBorder p="sm">
      <Group gap="sm" wrap="nowrap">
        {icon}
        <div>
          <Text size="xl" fw={600} lh={1}>
            {value}
          </Text>
          <Text size="xs" c="dimmed">
            {label}
          </Text>
        </div>
      </Group>
    </Paper>
  );
}
