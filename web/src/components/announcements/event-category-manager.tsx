import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Button, ColorInput, Group, Stack, Text, TextInput } from "@mantine/core";
import { Check, Pencil, Trash2, X } from "lucide-react";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createCompanyEventCategory,
  deleteCompanyEventCategory,
  updateCompanyEventCategory,
  type CompanyEventCategory,
} from "@/services/company-calendar.service";

export const EVENT_CATEGORIES_QUERY_KEY = "companyEventCategories";
const DEFAULT_COLOR = "#228be6";
const NAME_MAX = 60;

/** Add / rename+recolour / delete for event categories. A 409 on delete (category in use) surfaces the server message. */
export default function EventCategoryManager({ categories }: { categories: CompanyEventCategory[] }) {
  const { t } = useTranslation("companyCalendar");
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<string | "new" | null>(null);
  const [name, setName] = useState("");
  const [color, setColor] = useState(DEFAULT_COLOR);
  const [error, setError] = useState<string | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: [EVENT_CATEGORIES_QUERY_KEY] });
  const onError = (err: unknown) => setError(getApiErrorMessage(err, t("categories.genericError")));

  const save = useMutation({
    mutationFn: async () => {
      const input = { name: name.trim(), color };
      if (editing === "new") await createCompanyEventCategory(input);
      else await updateCompanyEventCategory(editing as string, input);
    },
    onSuccess: () => {
      setEditing(null);
      setError(null);
      void refresh();
    },
    onError,
  });
  const remove = useMutation({
    mutationFn: (id: string) => deleteCompanyEventCategory(id),
    onSuccess: () => {
      setError(null);
      void refresh();
    },
    onError,
  });

  function startEdit(category: CompanyEventCategory | null) {
    setEditing(category?.id ?? "new");
    setName(category?.name ?? "");
    setColor(category?.color ?? DEFAULT_COLOR);
    setError(null);
  }

  return (
    <Stack gap="xs" data-testid="event-category-manager">
      {categories.map((category) =>
        editing === category.id ? null : (
          <Group key={category.id} justify="space-between" wrap="nowrap">
            <Group gap="xs" wrap="nowrap">
              <span aria-hidden className="inline-block h-3 w-3 rounded-full" style={{ backgroundColor: category.color }} />
              <Text size="sm">{category.name}</Text>
            </Group>
            <Group gap={4} wrap="nowrap">
              <ActionIcon variant="subtle" aria-label={t("categories.edit", { name: category.name })} onClick={() => startEdit(category)}>
                <Pencil size={14} />
              </ActionIcon>
              <ActionIcon
                variant="subtle"
                color="red"
                aria-label={t("categories.delete", { name: category.name })}
                loading={remove.isPending && remove.variables === category.id}
                onClick={() => remove.mutate(category.id)}
              >
                <Trash2 size={14} />
              </ActionIcon>
            </Group>
          </Group>
        )
      )}
      {editing ? (
        <Group align="flex-end" wrap="nowrap" gap="xs">
          <TextInput
            label={t("categories.name")}
            value={name}
            maxLength={NAME_MAX}
            onChange={(e) => setName(e.currentTarget.value)}
            className="flex-1"
          />
          <ColorInput label={t("categories.color")} value={color} onChange={setColor} format="hex" withPicker={false} swatches={SWATCHES} w={130} />
          <ActionIcon size="lg" aria-label={t("categories.save")} loading={save.isPending} disabled={!name.trim()} onClick={() => save.mutate()}>
            <Check size={16} />
          </ActionIcon>
          <ActionIcon size="lg" variant="default" aria-label={t("categories.cancel")} onClick={() => setEditing(null)}>
            <X size={16} />
          </ActionIcon>
        </Group>
      ) : (
        <Button type="button" variant="light" size="xs" w="fit-content" onClick={() => startEdit(null)}>
          {t("categories.add")}
        </Button>
      )}
      {error && (
        <Text size="sm" c="red" role="alert">
          {error}
        </Text>
      )}
    </Stack>
  );
}

const SWATCHES = ["#228be6", "#fd7e14", "#e64980", "#12b886", "#7950f2", "#fab005", "#fa5252", "#495057"];
