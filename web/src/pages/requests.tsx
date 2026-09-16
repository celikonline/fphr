import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { Badge, Card, Group, SimpleGrid, Stack, Text, Title, UnstyledButton } from "@mantine/core";
import { CalendarDays, Clock, CreditCard, Receipt, Stamp, type LucideIcon } from "lucide-react";
import { usePermission } from "@/components/permission-guard";

interface RequestTypeCard {
  key: string;
  icon: LucideIcon;
  /** Undefined = available now; a number renders a "Faz N" badge and disables the card. */
  phase?: number;
  to?: string;
}

/** "+ Talep" için sunulacak türler (11.7 roadmap #42); yalnız bugün var olan modüller aktif, geri kalanı planlanan fazıyla görünür. */
const REQUEST_TYPES: RequestTypeCard[] = [
  { key: "leave", icon: CalendarDays, to: "/app/leave?new=1" },
  { key: "overtime", icon: Clock, to: "/app/leave/overtimes?new=1" },
  { key: "advance", icon: CreditCard, to: "/app/payments?new=advance" },
  { key: "expense", icon: Receipt, to: "/app/payments?new=expense" },
  { key: "visa", icon: Stamp, phase: 4 },
];

/**
 * Tek "Talep" ekranı ("nav.requests"): her talep türü kendi sayfasına `?new=1` ile yönlendirir ve o sayfa oradan
 * ekleme diyaloğunu otomatik açar - talep akışının kendisi tek bir yerde değil, ilgili modülde kalır.
 */
export default function RequestsPage() {
  const { t } = useTranslation(["requests", "navigation", "leave"]);
  const navigate = useNavigate();
  const canLeave = usePermission("leave.my.requests");
  const canOvertime = usePermission("leave.my.overtimes");
  const canAdvance = usePermission("expense.my.advances");
  const canExpense = usePermission("expense.my.claims");

  const availability: Record<string, boolean> = { leave: canLeave, overtime: canOvertime, advance: canAdvance, expense: canExpense, visa: true };
  const visible = REQUEST_TYPES.filter((type) => availability[type.key]);

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2">
          {t("navigation:nav.requests")}
        </Title>
        <Text c="dimmed">{t("requests:subtitle")}</Text>
      </div>

      {visible.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("requests:empty")}
        </Text>
      ) : (
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
          {visible.map((type) => {
            const disabled = type.phase !== undefined;
            const Icon = type.icon;
            return (
              <UnstyledButton
                key={type.key}
                disabled={disabled}
                onClick={() => type.to && navigate(type.to)}
                style={{ opacity: disabled ? 0.6 : 1, cursor: disabled ? "not-allowed" : "pointer" }}
              >
                <Card withBorder padding="lg" h="100%">
                  <Group justify="space-between" align="flex-start" wrap="nowrap">
                    <Stack gap={4}>
                      <Group gap="xs">
                        <Icon size={20} color="var(--mantine-primary-color-filled)" />
                        <Text fw={500}>{t(`requests:types.${type.key}.title`)}</Text>
                      </Group>
                      <Text size="sm" c="dimmed">
                        {t(`requests:types.${type.key}.description`)}
                      </Text>
                    </Stack>
                    {disabled && <Badge variant="light">{t("navigation:phase", { phase: type.phase })}</Badge>}
                  </Group>
                </Card>
              </UnstyledButton>
            );
          })}
        </SimpleGrid>
      )}
    </div>
  );
}
