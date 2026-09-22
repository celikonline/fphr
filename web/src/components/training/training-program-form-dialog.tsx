import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import {
  Button,
  Group,
  Input,
  Modal,
  NumberInput,
  Select,
  Stack,
  TextInput,
  Textarea,
} from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  TRAINING_CATEGORIES,
  createTrainingProgram,
  type TrainingCategory,
} from "@/services/training.service";

/** Server limit (TrainingProgram.Create): title 1..200. */
const TITLE_MAX_LENGTH = 200;

/** Local day → ISO instant at the given local time, so a program shows on the chosen day in the user's timezone. */
function dayToIso(day: string, time: string): string {
  return new Date(`${day}T${time}`).toISOString();
}

interface TrainingProgramFormDialogProps {
  onClose: () => void;
  onCreated: () => void;
}

/** Yeni eğitim programı (taslak) oluşturur; yayına alma listeden "Etkinleştir" ile yapılır. */
export default function TrainingProgramFormDialog({
  onClose,
  onCreated,
}: TrainingProgramFormDialogProps) {
  const { t } = useTranslation(["training", "common"]);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState<TrainingCategory>("compliance");
  const [startDay, setStartDay] = useState("");
  const [endDay, setEndDay] = useState("");
  const [capacity, setCapacity] = useState<number | "">("");

  const mutation = useMutation({
    mutationFn: () =>
      createTrainingProgram({
        title: title.trim(),
        description: description.trim() || null,
        category,
        startDate: dayToIso(startDay, "09:00:00"),
        endDate: dayToIso(endDay, "18:00:00"),
        maxCapacity: capacity === "" ? null : capacity,
      }),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("training:programs.created"),
      });
      onCreated();
      onClose();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("training:actionFailed")),
      }),
  });

  const endBeforeStart = startDay !== "" && endDay !== "" && endDay < startDay;
  const canSubmit =
    title.trim().length > 0 &&
    startDay !== "" &&
    endDay !== "" &&
    !endBeforeStart &&
    !mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={t("training:programs.create")} centered size="lg">
      <Stack gap="md">
        <TextInput
          id="training-title"
          label={t("training:fields.title")}
          value={title}
          maxLength={TITLE_MAX_LENGTH}
          required
          onChange={(e) => setTitle(e.currentTarget.value)}
        />
        <Textarea
          id="training-description"
          label={t("training:fields.description")}
          value={description}
          autosize
          minRows={3}
          onChange={(e) => setDescription(e.currentTarget.value)}
        />
        <Group grow align="flex-start">
          <Select
            id="training-category"
            label={t("training:fields.category")}
            data={TRAINING_CATEGORIES.map((value) => ({
              value,
              label: t(`training:categories.${value}`),
            }))}
            value={category}
            allowDeselect={false}
            onChange={(value) => value && setCategory(value as TrainingCategory)}
          />
          <NumberInput
            id="training-capacity"
            label={t("training:fields.capacity")}
            description={t("training:programs.capacityHint")}
            min={1}
            value={capacity}
            onChange={(value) => setCapacity(typeof value === "number" ? value : "")}
          />
        </Group>
        <Group grow align="flex-start">
          <Input.Wrapper label={t("training:fields.startDate")} required>
            <DatePicker id="training-start" value={startDay} onChange={setStartDay} />
          </Input.Wrapper>
          <Input.Wrapper
            label={t("training:fields.endDate")}
            required
            error={endBeforeStart ? t("training:programs.endBeforeStart") : undefined}
          >
            <DatePicker
              id="training-end"
              value={endDay}
              min={startDay || undefined}
              onChange={setEndDay}
            />
          </Input.Wrapper>
        </Group>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!canSubmit}
            loading={mutation.isPending}
          >
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
