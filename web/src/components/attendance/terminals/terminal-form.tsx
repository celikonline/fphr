import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Group, Modal, NativeSelect, Stack, TextInput } from "@mantine/core";
import {
  TERMINAL_DIRECTION_MODES,
  type Terminal,
  type TerminalDirectionMode,
  type TerminalGroup,
  type TerminalInput,
} from "@/services/attendance-terminals.service";

interface TerminalFormProps {
  /** null = yeni terminal. */
  terminal: Terminal | null;
  groups: TerminalGroup[];
  saving: boolean;
  onSubmit: (input: TerminalInput) => void;
  onClose: () => void;
}

/** Terminal oluşturma/düzenleme formu (yerel state; her açılışta yeniden bağlanır). */
export default function TerminalForm({ terminal, groups, saving, onSubmit, onClose }: TerminalFormProps) {
  const { t } = useTranslation(["terminals", "common"]);
  const [code, setCode] = useState(terminal?.code ?? "");
  const [name, setName] = useState(terminal?.name ?? "");
  const [groupId, setGroupId] = useState(terminal?.terminalGroupId ?? "");
  const [mode, setMode] = useState<TerminalDirectionMode>(terminal?.directionMode ?? "both");
  const [invalid, setInvalid] = useState(false);

  function submit() {
    if (!code.trim() || !name.trim()) {
      setInvalid(true);
      return;
    }
    onSubmit({
      code: code.trim(),
      name: name.trim(),
      terminalGroupId: groupId || null,
      locationId: terminal?.locationId ?? null,
      directionMode: mode,
    });
  }

  return (
    <Modal opened onClose={onClose} title={terminal ? t("terminals:terminal.edit") : t("terminals:terminal.new")}>
      <Stack gap="sm">
        <TextInput
          label={t("terminals:terminal.code")}
          value={code}
          maxLength={50}
          withAsterisk
          error={invalid && !code.trim() ? t("terminals:terminal.codeRequired") : undefined}
          onChange={(e) => setCode(e.currentTarget.value)}
        />
        <TextInput
          label={t("terminals:terminal.name")}
          value={name}
          withAsterisk
          error={invalid && !name.trim() ? t("terminals:terminal.codeRequired") : undefined}
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <NativeSelect
          label={t("terminals:terminal.group")}
          value={groupId}
          onChange={(e) => setGroupId(e.currentTarget.value)}
          data={[
            { value: "", label: t("terminals:terminal.noGroup") },
            ...groups.map((g) => ({ value: g.id, label: g.name })),
          ]}
        />
        <NativeSelect
          label={t("terminals:terminal.direction")}
          value={mode}
          onChange={(e) => setMode(e.currentTarget.value as TerminalDirectionMode)}
          data={TERMINAL_DIRECTION_MODES.map((m) => ({ value: m, label: t(`terminals:directionMode.${m}`) }))}
        />
        <Group justify="flex-end" mt="sm">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button loading={saving} onClick={submit}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
