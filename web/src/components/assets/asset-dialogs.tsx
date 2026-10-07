import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import {
  Button,
  Group,
  Input,
  Modal,
  NumberInput,
  Select,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Textarea,
} from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { decimalSeparatorOf } from "@/components/assets/asset-format";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import {
  ASSET_CONDITIONS,
  assignAsset,
  createAsset,
  markAssetLost,
  returnAsset,
  updateAsset,
  type Asset,
  type AssetCategory,
  type AssetCondition,
} from "@/services/assets.service";

const todayIso = () => toDateOnly(new Date());

/** Server-side limit of the return/lost note (`asset.note_too_long`). */
const NOTE_MAX_LENGTH = 500;

/** Narrow phones (<= 390px): dialogs take the whole screen instead of a cramped modal (same breakpoint as the card). */
function useNarrowScreen(): boolean {
  return useMediaQuery("(max-width: 390px)") ?? false;
}

function useActionToast() {
  const { t } = useTranslation(["assets", "common"]);
  return {
    success: (description: string) =>
      toast({ variant: "success", title: t("common:success"), description }),
    failure: (error: unknown) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("assets:section.errors.retry")),
      }),
  };
}

function conditionOptions(t: (key: string) => string) {
  return ASSET_CONDITIONS.map((value) => ({ value, label: t(`assets:enum.condition.${value}`) }));
}

interface AssetFormDialogProps {
  /** Asset being edited; null creates a new one. */
  asset: Asset | null;
  categories: AssetCategory[];
  onClose: () => void;
  onSaved: () => void;
}

/** Envantere zimmet ekler ya da künyesini düzenler (kategori sonradan değişmez - API'de yok). */
export function AssetFormDialog({ asset, categories, onClose, onSaved }: AssetFormDialogProps) {
  const { t, i18n } = useTranslation(["assets", "common"]);
  const notify = useActionToast();
  const isMobile = useNarrowScreen();
  const [categoryId, setCategoryId] = useState<string | null>(asset?.categoryId ?? null);
  const [name, setName] = useState(asset?.name ?? "");
  const [assetTag, setAssetTag] = useState(asset?.assetTag ?? "");
  const [serialNumber, setSerialNumber] = useState(asset?.serialNumber ?? "");
  const [brand, setBrand] = useState(asset?.brand ?? "");
  const [model, setModel] = useState(asset?.model ?? "");
  const [description, setDescription] = useState(asset?.description ?? "");
  const [purchaseDate, setPurchaseDate] = useState(asset?.purchaseDate ?? "");
  const [purchaseCost, setPurchaseCost] = useState<number | string>(asset?.purchaseCost ?? "");
  const [warrantyEndsOn, setWarrantyEndsOn] = useState(asset?.warrantyEndsOn ?? "");
  const [condition, setCondition] = useState<AssetCondition>(asset?.condition ?? "good");

  const mutation = useMutation({
    mutationFn: async () => {
      const fields = {
        name: name.trim(),
        serialNumber: serialNumber.trim() || null,
        description: description.trim() || null,
        assetTag: assetTag.trim() || null,
        brand: brand.trim() || null,
        model: model.trim() || null,
        purchaseDate: purchaseDate || null,
        purchaseCost: typeof purchaseCost === "number" ? purchaseCost : null,
        warrantyEndsOn: warrantyEndsOn || null,
      };
      if (asset) await updateAsset(asset.id, { ...fields, condition });
      else await createAsset({ ...fields, categoryId: categoryId! });
    },
    onSuccess: () => {
      notify.success(t("assets:inventory.saved"));
      onSaved();
      onClose();
    },
    onError: notify.failure,
  });

  const canSubmit =
    name.trim().length > 0 && (asset !== null || categoryId !== null) && !mutation.isPending;
  const busy = mutation.isPending;

  return (
    <Modal
      opened
      onClose={onClose}
      title={asset ? t("assets:inventory.editTitle") : t("assets:inventory.create")}
      size="36rem"
      centered={!isMobile}
      fullScreen={isMobile}
    >
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
          maxLength={255}
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
          <TextInput
            id="asset-tag"
            label={t("assets:section.assetTag")}
            description={t("assets:section.assetTagHint")}
            value={assetTag}
            maxLength={50}
            onChange={(e) => setAssetTag(e.currentTarget.value)}
          />
          <TextInput
            id="asset-serial"
            label={t("assets:section.serialNumber")}
            value={serialNumber}
            maxLength={100}
            onChange={(e) => setSerialNumber(e.currentTarget.value)}
          />
          <TextInput
            id="asset-brand"
            label={t("assets:section.brand")}
            value={brand}
            maxLength={100}
            onChange={(e) => setBrand(e.currentTarget.value)}
          />
          <TextInput
            id="asset-model"
            label={t("assets:section.model")}
            value={model}
            maxLength={100}
            onChange={(e) => setModel(e.currentTarget.value)}
          />
          <Input.Wrapper id="asset-purchaseDate" label={t("assets:section.purchaseDate")}>
            <DatePicker
              id="asset-purchaseDate"
              value={purchaseDate}
              onChange={setPurchaseDate}
              clearable
            />
          </Input.Wrapper>
          <NumberInput
            id="asset-purchaseCost"
            label={t("assets:section.purchaseCost")}
            value={purchaseCost}
            onChange={setPurchaseCost}
            min={0}
            decimalScale={2}
            decimalSeparator={decimalSeparatorOf(i18n.language)}
            thousandSeparator={decimalSeparatorOf(i18n.language) === "," ? "." : ","}
            hideControls
          />
          <Input.Wrapper id="asset-warrantyEndsOn" label={t("assets:section.warrantyEndsOn")}>
            <DatePicker
              id="asset-warrantyEndsOn"
              value={warrantyEndsOn}
              min={purchaseDate || undefined}
              onChange={setWarrantyEndsOn}
              clearable
            />
          </Input.Wrapper>
          {asset && (
            <Select
              id="asset-condition"
              label={t("assets:section.condition")}
              data={conditionOptions(t)}
              value={condition}
              onChange={(value) => value && setCondition(value as AssetCondition)}
              allowDeselect={false}
            />
          )}
        </SimpleGrid>
        <Textarea
          id="asset-description"
          label={t("assets:section.description")}
          value={description}
          autosize
          minRows={2}
          maxLength={1000}
          onChange={(e) => setDescription(e.currentTarget.value)}
        />
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={busy}>
            {t("common:cancel")}
          </Button>
          <Button onClick={() => mutation.mutate()} disabled={!canSubmit} loading={busy}>
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
  const isMobile = useNarrowScreen();
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
    <Modal
      opened
      onClose={onClose}
      title={t("assets:inventory.assignTitle", { name: asset.name })}
      centered={!isMobile}
      fullScreen={isMobile}
    >
      <Stack gap="md">
        <EmployeePicker
          id="asset-assign-employee"
          label={t("assets:inventory.employee")}
          value={employee}
          onChange={setEmployee}
          withAsterisk
        />
        <Input.Wrapper id="asset-assign-issuedAt" label={t("assets:section.issuedAt")} required>
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

export interface ReturnAssetValues {
  returnedAt: string;
  condition: AssetCondition;
  note?: string;
}

interface ReturnAssetFormProps {
  busy: boolean;
  /** Earliest selectable return date (the issue date). */
  minDate?: string;
  /** Preselected condition: the asset's current one. */
  defaultCondition?: AssetCondition;
  onCancel: () => void;
  onSubmit: (values: ReturnAssetValues) => void;
}

/** Return date + condition (good/fair/damaged) + optional note; shared by the inventory and the employee card. */
export function ReturnAssetForm({
  busy,
  minDate,
  defaultCondition,
  onCancel,
  onSubmit,
}: ReturnAssetFormProps) {
  const { t } = useTranslation(["assets", "common"]);
  const [returnedAt, setReturnedAt] = useState(todayIso());
  const [condition, setCondition] = useState<AssetCondition>(defaultCondition ?? "good");
  const [note, setNote] = useState("");

  function submit(event: FormEvent) {
    event.preventDefault();
    onSubmit({ returnedAt, condition, note: note.trim() || undefined });
  }

  return (
    <form onSubmit={submit}>
      <Stack gap="md">
        <Input.Wrapper id="asset-return-returnedAt" label={t("assets:section.returnedAt")} required>
          <DatePicker
            id="asset-return-returnedAt"
            value={returnedAt}
            min={minDate}
            disabled={busy}
            onChange={setReturnedAt}
          />
        </Input.Wrapper>
        <Select
          id="asset-return-condition"
          label={t("assets:section.returnCondition")}
          data={conditionOptions(t)}
          value={condition}
          onChange={(value) => value && setCondition(value as AssetCondition)}
          allowDeselect={false}
          disabled={busy}
        />
        <Textarea
          id="asset-return-note"
          label={t("assets:section.returnNote")}
          description={t("assets:section.returnNoteHint")}
          value={note}
          maxLength={NOTE_MAX_LENGTH}
          autosize
          minRows={2}
          disabled={busy}
          onChange={(e) => setNote(e.currentTarget.value)}
        />
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={onCancel} disabled={busy}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={busy} disabled={!returnedAt}>
            {t("assets:section.return")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

/** Atanmış zimmeti iade alır (tarih + durum + not); zimmet tekrar "Boşta" olur. */
export function ReturnAssetFromInventoryDialog({ asset, onClose, onDone }: AssetActionDialogProps) {
  const { t } = useTranslation(["assets", "common"]);
  const notify = useActionToast();
  const isMobile = useNarrowScreen();

  const mutation = useMutation({
    mutationFn: ({ returnedAt, condition, note }: ReturnAssetValues) =>
      returnAsset(asset.id, returnedAt, condition, note),
    onSuccess: () => {
      notify.success(t("assets:section.returned"));
      onDone();
      onClose();
    },
    onError: notify.failure,
  });

  return (
    <Modal
      opened
      onClose={onClose}
      title={t("assets:section.returnTitle", { name: asset.name })}
      centered={!isMobile}
      fullScreen={isMobile}
    >
      <ReturnAssetForm
        busy={mutation.isPending}
        minDate={asset.issuedAt ?? undefined}
        defaultCondition={asset.condition}
        onCancel={onClose}
        onSubmit={(values) => mutation.mutate(values)}
      />
    </Modal>
  );
}

/** Zimmeti kayıp olarak işaretler; zimmetliyse atama "kayıp" nedeniyle kapanır. Geri alınamaz, bu yüzden açık uyarı var. */
export function LostAssetDialog({ asset, onClose, onDone }: AssetActionDialogProps) {
  const { t } = useTranslation(["assets", "common"]);
  const notify = useActionToast();
  const isMobile = useNarrowScreen();
  const [occurredOn, setOccurredOn] = useState(todayIso());
  const [note, setNote] = useState("");

  const mutation = useMutation({
    mutationFn: () => markAssetLost(asset.id, occurredOn, note.trim() || undefined),
    onSuccess: () => {
      notify.success(t("assets:lost.done"));
      onDone();
      onClose();
    },
    onError: notify.failure,
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    mutation.mutate();
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={t("assets:lost.title", { name: asset.name })}
      centered={!isMobile}
      fullScreen={isMobile}
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {asset.status === "assigned"
              ? t("assets:lost.descriptionAssigned")
              : t("assets:lost.description")}
          </Text>
          <Input.Wrapper id="asset-lost-occurredOn" label={t("assets:lost.occurredOn")} required>
            <DatePicker
              id="asset-lost-occurredOn"
              value={occurredOn}
              min={asset.issuedAt ?? undefined}
              disabled={mutation.isPending}
              onChange={setOccurredOn}
            />
          </Input.Wrapper>
          <Textarea
            id="asset-lost-note"
            label={t("assets:lost.note")}
            description={t("assets:lost.noteHint")}
            value={note}
            maxLength={NOTE_MAX_LENGTH}
            autosize
            minRows={2}
            disabled={mutation.isPending}
            onChange={(e) => setNote(e.currentTarget.value)}
          />
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" color="red" loading={mutation.isPending} disabled={!occurredOn}>
              {t("assets:lost.confirm")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
