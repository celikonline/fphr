import { useTranslation } from "react-i18next";
import { Group, Text } from "@mantine/core";
import {
  MATRIX_CELL_CODES,
  type MatrixCellCode,
} from "@/services/attendance-monthly-matrix.service";

/** Mantine renk paleti adı; `light` varyantı açık/koyu temada kendiliğinden uyum sağlar. */
export const MATRIX_CODE_COLOR: Record<MatrixCellCode, string> = {
  full: "green",
  overtime: "blue",
  missing: "orange",
  absent: "red",
  weeklyRest: "gray",
  restDay: "gray",
  publicHoliday: "violet",
  paidLeave: "teal",
  unpaidLeave: "pink",
  otherLeave: "cyan",
  incalculable: "yellow",
  none: "gray",
};

/** Hücre / lejant rozeti: renk + kısa kod metni (renk tek başına bilgi taşımaz). */
export function MatrixChip({
  code,
  testId,
  size = 26,
}: {
  code: MatrixCellCode;
  testId?: string;
  size?: number;
}) {
  const { t } = useTranslation("attendance-daily");
  const color = MATRIX_CODE_COLOR[code];
  return (
    <span
      data-testid={testId}
      data-code={code}
      data-color={color}
      title={t(`monthlyMatrix.codes.${code}`)}
      className="inline-flex items-center justify-center rounded-sm text-xs font-semibold"
      style={{
        minWidth: size,
        height: size,
        backgroundColor: `var(--mantine-color-${color}-light)`,
        color: `var(--mantine-color-${color}-light-color)`,
        border: code === "none" ? "1px dashed var(--mantine-color-default-border)" : undefined,
      }}
    >
      {t(`monthlyMatrix.short.${code}`)}
    </span>
  );
}

/** Tüm durum kodlarını kısa kod + ad ile listeleyen lejant. */
export default function MatrixLegend() {
  const { t } = useTranslation("attendance-daily");
  return (
    <Group gap="md" wrap="wrap" aria-label={t("monthlyMatrix.legend")} role="list">
      {MATRIX_CELL_CODES.map((code) => (
        <Group
          key={code}
          gap={6}
          wrap="nowrap"
          role="listitem"
          data-testid={`matrix-legend-${code}`}
        >
          <MatrixChip code={code} />
          <Text size="sm">{t(`monthlyMatrix.codes.${code}`)}</Text>
        </Group>
      ))}
    </Group>
  );
}
