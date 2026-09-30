import { useTranslation } from "react-i18next";
import { ActionIcon, Tooltip } from "@mantine/core";
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

  const chipStyle = category
    ? { backgroundColor: `${category.color}26`, borderInlineStart: `3px solid ${category.color}` }
    : undefined;
  const content = (
    <>
      {category && <span className="sr-only">{category.name}: </span>}
      {time && <span className="font-semibold">{time} </span>}
      {entry.title}
    </>
  );
  const innerClass = "min-w-0 flex-1 truncate px-1.5 py-0.5 text-left";

  // Edit and delete are siblings (never nested interactive elements) inside one wrapper.
  return (
    <Tooltip label={tooltip} multiline maw={260}>
      <div
        className={`${classes.chip} flex items-center gap-0.5`}
        data-testid="calendar-event-chip"
        data-type={entry.type}
        data-category-color={category?.color}
        style={chipStyle}
      >
        {editable ? (
          <button
            type="button"
            className={innerClass}
            aria-label={t("editEvent", { name: entry.title })}
            onClick={() => onOpen(entry)}
          >
            {content}
          </button>
        ) : (
          <div className={innerClass}>
            {content}
          </div>
        )}
        {editable && (
          <ActionIcon size="xs" variant="transparent" aria-label={t("deleteEvent", { name: entry.title })} onClick={() => onDelete(entry)}>
            <Trash2 size={11} />
          </ActionIcon>
        )}
      </div>
    </Tooltip>
  );
}
