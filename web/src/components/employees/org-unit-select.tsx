import { useTranslation } from "react-i18next";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useOrgUnits } from "./use-org-units";

const EMPTY_VALUE = "__none__";

interface OrgUnitSelectProps {
  id?: string;
  value: string | null;
  onChange: (value: string | null) => void;
  /** Label of the "no unit" option (e.g. "Tüm birimler" for filters, "Birim yok" for forms). */
  emptyLabel: string;
  disabled?: boolean;
  className?: string;
}

export default function OrgUnitSelect({ id, value, onChange, emptyLabel, disabled, className }: OrgUnitSelectProps) {
  const { t } = useTranslation(["employees"]);
  const { options, nameById } = useOrgUnits();
  const unknownValue = value && !nameById.has(value);

  return (
    <Select value={value ?? EMPTY_VALUE} onValueChange={(v) => onChange(v === EMPTY_VALUE ? null : v)} disabled={disabled}>
      <SelectTrigger id={id} className={className}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={EMPTY_VALUE}>{emptyLabel}</SelectItem>
        {unknownValue && <SelectItem value={value}>{t("employees:unknownUnit")}</SelectItem>}
        {options.map((unit) => (
          <SelectItem key={unit.id} value={unit.id}>
            <span style={{ paddingLeft: `${unit.depth * 12}px` }}>{unit.name}</span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
