import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Group, Modal, Text } from "@mantine/core";
import EmployeeMultiPicker from "@/components/employees/employee-multi-picker";
import type { EmployeeSelection } from "@/types";
import { isSelectionEmpty } from "./selection-summary";

interface SelectionFieldProps {
  value: EmployeeSelection | null;
  onChange: (selection: EmployeeSelection | null) => void;
}

/** Opens the F1 multi-employee picker in a modal and shows a one-line summary of the chosen selection. */
export default function SelectionField({ value, onChange }: SelectionFieldProps) {
  const { t } = useTranslation("messages");
  const [opened, setOpened] = useState(false);
  const empty = isSelectionEmpty(value);

  const summary = empty
    ? null
    : value?.filter
      ? t("compose.selectionFilter", { excluded: value.excludedEmployeeIds?.length ?? 0 })
      : t("compose.selectionIds", { count: value?.employeeIds?.length ?? 0 });

  return (
    <>
      <Group gap="sm">
        <Button variant="light" onClick={() => setOpened(true)}>
          {empty ? t("compose.pickEmployees") : t("compose.changeEmployees")}
        </Button>
        {summary && <Text size="sm">{summary}</Text>}
        {!empty && (
          <Button variant="subtle" size="xs" color="gray" onClick={() => onChange(null)}>
            {t("compose.clearEmployees")}
          </Button>
        )}
      </Group>
      <Modal
        opened={opened}
        onClose={() => setOpened(false)}
        title={t("compose.pickerTitle")}
        size="xl"
      >
        <EmployeeMultiPicker
          onConfirm={(selection) => {
            onChange(selection);
            setOpened(false);
          }}
        />
      </Modal>
    </>
  );
}
