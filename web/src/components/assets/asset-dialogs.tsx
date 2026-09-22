import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Group, Input, Modal, Select, Stack, Text, TextInput, Textarea } from "@mantine/core";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  assignAsset,
  createAsset,
  returnAsset,
  updateAsset,
  type Asset,
  type AssetCategory,
} from "@/services/assets.service";

/** Local calendar day as "yyyy-MM-dd" (DateOnly contract). */
export function todayIso(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

function useActionToast() {
  const { t } = useTranslation(["assets", "common"]);
  return {
    success: (description: string) => toast({ variant: "success", title: t("common:success"), description }),
    failure: (error: unknown) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("assets:section.errors.generic")),
      }),
  };
}

interface AssetFormDialogProps {
  /** Asset being edited; null creates a new one. */
  asset: Asset | null;
  categories: AssetCategory[];
  onClose: () => void;
  onSaved: () => void;
}

/** Envantere zimmet ekler ya da ad/seri no/açıklamasını düzenler (kategori sonradan değişmez - API'de yok). */
export function AssetFormDialog({ asset, categories, onClose, onSaved }: AssetFormDialogProps) {
  const { t } = useTranslation(["assets", "common"]);
  const notify = useActionToast();
  const [categoryId, setCategoryId] = useState<string | null>(asset?.categoryId ?? null);
  const [name, setName] = useState(asset?.name ?? "");
  const [serialNumber, setSerialNumber] = useState(asset?.serialNumber ?? "");
  const [description, setDescription] = useState(asset?.description ?? "");

  const mutation = useMutation({
    mutationFn: async () => {
      const fields = {
        name: name.trim(),
        serialNumber: serialNumber.trim() || null,
        description: description.trim() || null,
      };
      if (asset) await updateAsset(asset.id, fields);
      else await createAsset({ ...fields, categoryId: categoryId! });
    },
    onSuccess: () => {
      notify.success(t("assets:inventory.saved"));
      onSaved();
      onClose();
    },
    onError: notify.failure,
  });

  const canSubmit = name.trim().length > 0 && (asset !== null || categoryId !== null) && !mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={asset ? t("assets:inventory.editTitle") : t("assets:inventory.create")} centered>
      <Stack gap="md">
        {categories.length === 0 && !asset ? (
          <Text size="sm" c="dimmed">
            {t("assets:section.noCategories")}
          </Text>
        ) : (
          <Select
            id="asset-category"
            label={t("assets:section.category")}
            data={categories.map((c) => ({ value: c.id, label: c.name }))}
            value={categoryId}
            onChange={setCategoryId}
            disabled={asset !== null}
            required
          />
        )}
        <TextInput
          id="asset-name"
          label={t("assets:section.name")}
          value={name}
          required
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <TextInput
          id="asset-serial"
          label={t("assets:section.serialNumber")}
          value={serialNumber}
          onChange={(e) => setSerialNumber(e.currentTarget.value)}
        />
        <Textarea
          id="asset-description"
          label={t("assets:section.description")}
          value={description}
          autosize
          minRows={2}
          onChange={(e) => setDescription(e.currentTarget.value)}
        />
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button onClick={() => mutation.mutate()} disabled={!canSubmit} loading={mutation.isPending}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

interface AssetActionDialogProps {
  asset: Asset;
  onClose: () => void;
  onDone: () => void;
}

/** Boştaki zimmeti bir çalışana atar (veriliş tarihiyle). */
export function AssignAssetFromInventoryDialog({ asset, onClose, onDone }: AssetActionDialogProps) {
  const { t } = useTranslation(["assets", "common"]);
  const notify = useActionToast();
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [issuedAt, setIssuedAt] = useState(todayIso());

  const mutation = useMutation({
    mutationFn: () => assignAsset(asset.id, employee!.id, issuedAt),
    onSuccess: () => {
      notify.success(t("assets:section.assigned"));
      onDone();
      onClose();
    },
    onError: notify.failure,
  });

  return (
    <Modal opened onClose={onClose} title={t("assets:inventory.assignTitle", { name: asset.name })} centered>
      <Stack gap="md">
        <EmployeePicker
          id="asset-assign-employee"
          label={t("assets:inventory.employee")}
          value={employee}
          onChange={setEmployee}
          withAsterisk
        />
        <Input.Wrapper label={t("assets:section.issuedAt")} required>
          <DatePicker id="asset-assign-issuedAt" value={issuedAt} onChange={setIssuedAt} />
        </Input.Wrapper>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!employee || !issuedAt || mutation.isPending}
            loading={mutation.isPending}
          >
            {t("assets:section.assign")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Atanmış zimmeti iade alır (iade tarihiyle); zimmet tekrar "Boşta" olur. */
export function ReturnAssetFromInventoryDialog({ asset, onClose, onDone }: AssetActionDialogProps) {
  const { t } = useTranslation(["assets", "common"]);
  const notify = useActionToast();
  const [returnedAt, setReturnedAt] = useState(todayIso());

  const mutation = useMutation({
    mutationFn: () => returnAsset(asset.id, returnedAt),
    onSuccess: () => {
      notify.success(t("assets:section.returned"));
      onDone();
      onClose();
    },
    onError: notify.failure,
  });

  return (
    <Modal opened onClose={onClose} title={t("assets:section.returnTitle", { name: asset.name })} centered>
      <Stack gap="md">
        <Input.Wrapper label={t("assets:section.returnedAt")} required>
          <DatePicker
            id="asset-return-returnedAt"
            value={returnedAt}
            min={asset.issuedAt ?? undefined}
            onChange={setReturnedAt}
          />
        </Input.Wrapper>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button onClick={() => mutation.mutate()} disabled={!returnedAt || mutation.isPending} loading={mutation.isPending}>
            {t("assets:section.return")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
