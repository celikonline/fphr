import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import {
  Button,
  Group,
  Modal,
  NumberInput,
  Skeleton,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
} from "@mantine/core";
import { ConfirmModal } from "@/components/ui/confirm-modal";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createOnboardingCategory,
  deleteOnboardingCategory,
  listOnboardingCategories,
  updateOnboardingCategory,
  type OnboardingCategory,
} from "@/services/onboarding.service";

export const CATEGORIES_KEY = ["onboarding", "categories"] as const;
/** Sensible bound for the free-text category name. */
const NAME_MAX_LENGTH = 200;

/** Kategori yönetimi (onboarding.templates.manage): liste, ekle/düzenle/sil. */
export default function OnboardingCategories() {
  const { t } = useTranslation(["onboarding", "common"]);
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<OnboardingCategory | "new" | null>(null);
  const [deleting, setDeleting] = useState<OnboardingCategory | null>(null);
  const query = useQuery({ queryKey: CATEGORIES_KEY, queryFn: listOnboardingCategories });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: CATEGORIES_KEY });
  const notifyError = (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("onboarding:actionFailed")),
    });

  const deleteMutation = useMutation({
    mutationFn: deleteOnboardingCategory,
    onSuccess: refresh,
    onError: notifyError,
  });

  if (query.isLoading) return <Skeleton height={160} />;
  if (query.isError) return <Text c="red">{t("onboarding:loadError")}</Text>;
  const rows = [...(query.data ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t("onboarding:categories.hint")}
        </Text>
        <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setEditing("new")}>
          {t("onboarding:categories.add")}
        </Button>
      </Group>
      {rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("onboarding:categories.empty")}
        </Text>
      ) : (
        <Table striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("onboarding:categories.name")}</Table.Th>
              <Table.Th>{t("onboarding:categories.sortOrder")}</Table.Th>
              <Table.Th>{t("onboarding:fields.active")}</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((category) => (
              <Table.Tr key={category.id} opacity={category.isActive ? 1 : 0.55}>
                <Table.Td>{category.name}</Table.Td>
                <Table.Td>{category.sortOrder}</Table.Td>
                <Table.Td>{category.isActive ? t("common:yes") : t("common:no")}</Table.Td>
                <Table.Td>
                  <Group gap="xs" justify="flex-end">
                    <Button size="compact-sm" variant="subtle" onClick={() => setEditing(category)}>
                      {t("onboarding:templates.edit")}
                    </Button>
                    <Button
                      size="compact-sm"
                      variant="subtle"
                      color="red"
                      loading={deleteMutation.isPending && deleteMutation.variables === category.id}
                      onClick={() => setDeleting(category)}
                    >
                      {t("onboarding:categories.delete")}
                    </Button>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      <ConfirmModal
        opened={deleting !== null}
        onClose={() => setDeleting(null)}
        title={t("onboarding:categories.delete")}
        message={t("onboarding:categories.confirmDelete", { name: deleting?.name ?? "" })}
        confirmLabel={t("onboarding:categories.delete")}
        danger
        onConfirm={async () => {
          if (deleting) await deleteMutation.mutateAsync(deleting.id).catch(() => undefined);
        }}
      />
      {editing && (
        <CategoryDialog
          category={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={refresh}
          nextSortOrder={rows.length}
        />
      )}
    </Stack>
  );
}

interface CategoryDialogProps {
  category: OnboardingCategory | null;
  nextSortOrder: number;
  onClose: () => void;
  onSaved: () => void;
}

function CategoryDialog({ category, nextSortOrder, onClose, onSaved }: CategoryDialogProps) {
  const { t } = useTranslation(["onboarding", "common"]);
  const [name, setName] = useState(category?.name ?? "");
  const [sortOrder, setSortOrder] = useState<number>(category?.sortOrder ?? nextSortOrder);
  const [isActive, setIsActive] = useState(category?.isActive ?? true);
  const mutation = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), sortOrder, isActive };
      return category
        ? updateOnboardingCategory(category.id, input)
        : createOnboardingCategory(input);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("onboarding:actionFailed")),
      }),
  });

  return (
    <Modal
      opened
      onClose={onClose}
      centered
      title={t(category ? "onboarding:categories.editTitle" : "onboarding:categories.addTitle")}
    >
      <Stack gap="md">
        <TextInput
          label={t("onboarding:categories.name")}
          value={name}
          maxLength={NAME_MAX_LENGTH}
          required
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <NumberInput
          label={t("onboarding:categories.sortOrder")}
          value={sortOrder}
          allowDecimal={false}
          onChange={(value) => setSortOrder(typeof value === "number" ? value : 0)}
        />
        {category && (
          <Switch
            label={t("onboarding:fields.active")}
            checked={isActive}
            onChange={(e) => setIsActive(e.currentTarget.checked)}
          />
        )}
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!name.trim()}
            loading={mutation.isPending}
          >
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
