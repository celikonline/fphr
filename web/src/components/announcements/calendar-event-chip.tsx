import { useTranslation } from "react-i18next";
import { ActionIcon, Badge, Tooltip } from "@mantine/core";
import { Trash2 } from "lucide-react";
import type { CompanyCalendarEntry, CompanyEventCategory } from "@/services/company-calendar.service";
import { eventTimeRange } from "./event-form-model";
import classes from "@/pages/company-calendar.module.css";

interface CalendarEventChipProps {
  entry: CompanyCalendarEntry;
  typeLabel: string;
  category?: CompanyEventCategory;
  canManage: boolean;
  onOpen: (entry: CompanyCalendarEntry) => void;
  onDelete: (entry: CompanyCalendarEntry) => void;
}

/**
 * Calendar chip: category colour (falls back to the type colour), time range for timed events. Colour is never the
 * only signal: the category name is in the tooltip and in a screen-reader-only prefix.
 */
export default function CalendarEventChip({ entry, typeLabel, category, canManage, onOpen, onDelete }: CalendarEventChipProps) {
  const { t, i18n } = useTranslation("companyCalendar");
  const editable = canManage && Boolean(entry.id);
  const time = eventTimeRange(entry, i18n.language);
  const details = [
    category?.name,
    entry.mode === "online" ? entry.onlineUrl : entry.mode === "onsite" ? entry.location : null,
  ].filter(Boolean);
  const tooltip = [`${typeLabel}: ${entry.title}`, time, ...details].filter(Boolean).join(" · ");

  return (
    <Tooltip label={tooltip} multiline maw={260}>
      <Badge
        className={classes.chip}
        data-type={entry.type}
        data-category-color={category?.color}
        data-testid="calendar-event-chip"
        variant="light"
        radius="sm"
        component={editable ? "button" : "div"}
        type={editable ? "button" : undefined}
        onClick={editable ? () => onOpen(entry) : undefined}
        style={category ? { backgroundColor: `${category.color}26`, borderInlineStart: `3px solid ${category.color}` } : undefined}
        styles={{
          root: { display: "block", width: "100%", cursor: editable ? "pointer" : "default", ...(category ? { color: "inherit" } : {}) },
          label: { overflow: "hidden", textOverflow: "ellipsis" },
        }}
        rightSection={
          editable ? (
            <ActionIcon
              size="xs"
              variant="transparent"
              aria-label={t("delete")}
              onClick={(event) => {
                event.stopPropagation();
                onDelete(entry);
              }}
            >
              <Trash2 size={11} />
            </ActionIcon>
          ) : undefined
        }
      >
        {category && <span className="sr-only">{category.name}: </span>}
        {time && <span className="font-semibold">{time} </span>}
        {entry.title}
      </Badge>
    </Tooltip>
  );
}
