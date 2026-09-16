import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Badge,
  Button,
  Card,
  Drawer,
  Group,
  Modal,
  NumberInput,
  Select,
  Skeleton,
  Stack,
  Tabs,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { CalendarRange, Plus, Trash2 } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  activateReviewCycle,
  closeReviewCycle,
  createReviewCycle,
  createReviewForm,
  getMyReviewCycles,
  listReviewCycles,
  listReviewForms,
  publishReviewForm,
  type ReviewCycle,
  type ReviewCycleStatus,
  type ReviewForm,
} from "@/services/performance.service";

const STATUS_BADGE: Record<ReviewCycleStatus, { color?: string; variant: "light" | "filled" | "outline" }> = {
  draft: { variant: "outline", color: "gray" },
  active: { variant: "filled", color: "blue" },
  closed: { variant: "light", color: "gray" },
};

const FORM_STATUS_BADGE: Record<ReviewForm["status"], { color?: string; variant: "light" | "filled" | "outline" }> = {
  draft: { variant: "outline", color: "gray" },
  published: { variant: "filled", color: "green" },
};

function reviewCycleErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "performance.cycle.invalid_date_range") return t("performance:reviewCycles.admin.errors.invalidDateRange");
  if (problem?.code === "performance.cycle.name_required") return t("performance:reviewCycles.admin.errors.nameRequired");
  return getApiErrorMessage(error, t("performance:genericError"));
}

function reviewFormErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "performance.form.weights_must_sum_to_100") return t("performance:reviewForms.errors.weightsMustSumTo100");
  if (problem?.code === "performance.form.title_required") return t("performance:reviewForms.errors.titleRequired");
  if (problem?.code === "performance.form.question_text_required") return t("performance:reviewForms.errors.questionTextRequired");
  if (problem?.code === "performance.review.immutable") return t("performance:reviewForms.errors.cycleImmutable");
  return getApiErrorMessage(error, t("performance:genericError"));
}

function ReviewCycleStatusBadge({ status }: { status: ReviewCycleStatus }) {
  const { t } = useTranslation(["performance"]);
  return <Badge {...STATUS_BADGE[status]}>{t(`performance:reviewCycles.status.${status}`)}</Badge>;
}

export default function ReviewCyclesPage() {
  const canAccess = useAnyPermission(["performance.my.reviews", "performance.reviews.view"]);
  if (!canAccess) return <NoAccess />;
  return <ReviewCycles />;
}

function ReviewCycles() {
  const { t } = useTranslation(["performance", "common"]);
  const canMine = usePermission("performance.my.reviews");
  const canManageView = usePermission("performance.reviews.view");

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-4 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <CalendarRange className="h-6 w-6" /> {t("performance:reviewCycles.title")}
        </Title>
        <Text c="dimmed">{t("performance:reviewCycles.subtitle")}</Text>
      </div>

      {canMine && canManageView ? (
        <Tabs defaultValue="mine" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="mine">{t("performance:reviewCycles.tabs.mine")}</Tabs.Tab>
            <Tabs.Tab value="all">{t("performance:reviewCycles.tabs.all")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="mine" pt="md">
            <MyReviewCycles />
          </Tabs.Panel>
          <Tabs.Panel value="all" pt="md">
            <AdminReviewCycles />
          </Tabs.Panel>
        </Tabs>
      ) : canManageView ? (
        <AdminReviewCycles />
      ) : (
        <MyReviewCycles />
      )}
    </div>
  );
}

function MyReviewCycles() {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const cyclesQuery = useQuery({ queryKey: ["performance", "review-cycles", "me"], queryFn: getMyReviewCycles });

  const date = (value: string) => formatDateOnly(value, i18n.language);

  if (cyclesQuery.isLoading) {
    return (
      <Stack gap="md">
        <Skeleton h={90} />
        <Skeleton h={90} />
      </Stack>
    );
  }
  if (cyclesQuery.isError) {
    return (
      <Text size="sm" c="red">
        {getApiErrorMessage(cyclesQuery.error, t("performance:genericError"))}
      </Text>
    );
  }
  if (!cyclesQuery.data || cyclesQuery.data.length === 0) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed" ta="center">
          {t("performance:reviewCycles.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="md">
      {cyclesQuery.data.map((cycle) => (
        <Card key={cycle.id} withBorder padding="lg">
          <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
            <div>
              <Text fw={600}>{cycle.name}</Text>
              <Text size="sm" c="dimmed">
                {t("performance:reviewCycles.dateRange", { start: date(cycle.startDate), end: date(cycle.endDate) })}
              </Text>
            </div>
            <ReviewCycleStatusBadge status={cycle.status} />
          </Group>
        </Card>
      ))}
    </Stack>
  );
}

const STATUS_FILTER_OPTIONS: { value: string; labelKey: string }[] = [
  { value: "", labelKey: "performance:reviewCycles.admin.filter.all" },
  { value: "draft", labelKey: "performance:reviewCycles.status.draft" },
  { value: "active", labelKey: "performance:reviewCycles.status.active" },
  { value: "closed", labelKey: "performance:reviewCycles.status.closed" },
];

function AdminReviewCycles() {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("performance.reviews.manage");
  const canApprove = usePermission("performance.reviews.approve");

  const [statusFilter, setStatusFilter] = useState<ReviewCycleStatus | "">("");
  const [createOpen, setCreateOpen] = useState(false);
  const [closeTarget, setCloseTarget] = useState<ReviewCycle | null>(null);
  const [formsTarget, setFormsTarget] = useState<ReviewCycle | null>(null);

  const cyclesQuery = useQuery({
    queryKey: ["performance", "review-cycles", "all", statusFilter],
    queryFn: () => listReviewCycles(statusFilter || undefined),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["performance", "review-cycles"] });
  };

  const activateMutation = useMutation({
    mutationFn: (id: string) => activateReviewCycle(id),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("performance:reviewCycles.admin.activated") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: reviewCycleErrorMessage(err, t) }),
  });

  const closeMutation = useMutation({
    mutationFn: (id: string) => closeReviewCycle(id),
    onSuccess: () => {
      setCloseTarget(null);
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("performance:reviewCycles.admin.closed") });
    },
    onError: (err) => {
      setCloseTarget(null);
      toast({ variant: "destructive", title: t("common:error"), description: reviewCycleErrorMessage(err, t) });
    },
  });

  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center" gap="sm" wrap="wrap">
        <Select
          aria-label={t("performance:reviewCycles.admin.filter.label")}
          value={statusFilter}
          onChange={(value) => setStatusFilter((value ?? "") as ReviewCycleStatus | "")}
          data={STATUS_FILTER_OPTIONS.map((option) => ({ value: option.value, label: t(option.labelKey) }))}
          w={200}
        />
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setCreateOpen(true)}>
            {t("performance:reviewCycles.admin.create")}
          </Button>
        )}
      </Group>

      {cyclesQuery.isLoading ? (
        <Stack gap="md">
          <Skeleton h={90} />
          <Skeleton h={90} />
        </Stack>
      ) : cyclesQuery.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(cyclesQuery.error, t("performance:genericError"))}
        </Text>
      ) : !cyclesQuery.data || cyclesQuery.data.length === 0 ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="dimmed" ta="center">
            {t("performance:reviewCycles.admin.empty")}
          </Text>
        </Card>
      ) : (
        <Stack gap="md">
          {cyclesQuery.data.map((cycle) => (
            <Card key={cycle.id} withBorder padding="lg">
              <Stack gap="sm">
                <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
                  <div>
                    <Text fw={600}>{cycle.name}</Text>
                    <Text size="sm" c="dimmed">
                      {t("performance:reviewCycles.dateRange", { start: date(cycle.startDate), end: date(cycle.endDate) })}
                    </Text>
                  </div>
                  <ReviewCycleStatusBadge status={cycle.status} />
                </Group>
                <Group justify="flex-end" gap="sm" wrap="wrap">
                  <Button type="button" size="xs" variant="default" onClick={() => setFormsTarget(cycle)}>
                    {t("performance:reviewForms.openButton")}
                  </Button>
                  {canManage && cycle.status === "draft" && (
                    <Button
                      type="button"
                      size="xs"
                      loading={activateMutation.isPending && activateMutation.variables === cycle.id}
                      onClick={() => activateMutation.mutate(cycle.id)}
                    >
                      {t("performance:reviewCycles.admin.activate")}
                    </Button>
                  )}
                  {canApprove && cycle.status === "active" && (
                    <Button type="button" size="xs" color="red" variant="outline" onClick={() => setCloseTarget(cycle)}>
                      {t("performance:reviewCycles.admin.close")}
                    </Button>
                  )}
                </Group>
              </Stack>
            </Card>
          ))}
        </Stack>
      )}

      {canManage && <CreateCycleModal open={createOpen} onClose={() => setCreateOpen(false)} onCreated={invalidate} />}

      <ConfirmDialog
        open={Boolean(closeTarget)}
        onOpenChange={(open) => !open && setCloseTarget(null)}
        title={t("performance:reviewCycles.admin.closeConfirmTitle")}
        description={t("performance:reviewCycles.admin.closeConfirmDescription", { name: closeTarget?.name ?? "" })}
        confirmLabel={t("performance:reviewCycles.admin.close")}
        isLoading={closeMutation.isPending}
        onConfirm={() => closeTarget && closeMutation.mutate(closeTarget.id)}
      />

      {formsTarget && <ReviewFormsDrawer cycle={formsTarget} canManage={canManage} onClose={() => setFormsTarget(null)} />}
    </Stack>
  );
}

function CreateCycleModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: () => void }) {
  const { t } = useTranslation(["performance", "common"]);
  const [name, setName] = useState("");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [submitted, setSubmitted] = useState(false);

  const nameMissing = submitted && name.trim().length === 0;
  const rangeInvalid = submitted && Boolean(startDate) && Boolean(endDate) && endDate <= startDate;
  const datesMissing = submitted && (!startDate || !endDate);

  const mutation = useMutation({
    mutationFn: () => createReviewCycle({ name: name.trim(), startDate, endDate }),
    onSuccess: () => {
      onCreated();
      toast({ variant: "success", title: t("common:success"), description: t("performance:reviewCycles.admin.created") });
      close();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: reviewCycleErrorMessage(err, t) }),
  });

  function close() {
    setName("");
    setStartDate("");
    setEndDate("");
    setSubmitted(false);
    onClose();
  }

  function submit() {
    setSubmitted(true);
    if (name.trim().length === 0 || !startDate || !endDate || endDate <= startDate) return;
    mutation.mutate();
  }

  return (
    <Modal opened={open} onClose={close} title={t("performance:reviewCycles.admin.createTitle")}>
      <Stack gap="md">
        <TextInput
          data-autofocus
          label={t("performance:reviewCycles.admin.fields.name")}
          withAsterisk
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
          error={nameMissing ? t("common:required") : undefined}
        />
        <div>
          <Text size="sm" fw={500} mb={4}>
            {t("performance:reviewCycles.admin.fields.startDate")} <span aria-hidden>*</span>
          </Text>
          <DatePicker value={startDate} onChange={setStartDate} />
        </div>
        <div>
          <Text size="sm" fw={500} mb={4}>
            {t("performance:reviewCycles.admin.fields.endDate")} <span aria-hidden>*</span>
          </Text>
          <DatePicker value={endDate} onChange={setEndDate} min={startDate || undefined} />
          {rangeInvalid && (
            <Text size="xs" c="red" mt={4}>
              {t("performance:reviewCycles.admin.errors.invalidDateRange")}
            </Text>
          )}
          {!rangeInvalid && datesMissing && (
            <Text size="xs" c="red" mt={4}>
              {t("common:required")}
            </Text>
          )}
        </div>
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={close} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={mutation.isPending} onClick={submit}>
            {t("performance:reviewCycles.admin.createSubmit")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

interface QuestionRow {
  text: string;
  weight: number;
}

function ReviewFormsDrawer({ cycle, canManage, onClose }: { cycle: ReviewCycle; canManage: boolean; onClose: () => void }) {
  const { t } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);

  const formsQuery = useQuery({
    queryKey: ["performance", "review-forms", cycle.id],
    queryFn: () => listReviewForms(cycle.id),
  });

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: ["performance", "review-forms", cycle.id] });

  const publishMutation = useMutation({
    mutationFn: (id: string) => publishReviewForm(id),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("performance:reviewForms.published") });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: reviewFormErrorMessage(err, t) }),
  });

  const canCreate = canManage && cycle.status !== "closed";

  return (
    <Drawer
      opened
      onClose={onClose}
      position="right"
      size="28rem"
      title={
        <Text component="span" fw={600} size="lg">
          {t("performance:reviewForms.title", { name: cycle.name })}
        </Text>
      }
    >
      <Stack gap="lg">
        {canCreate && (
          <Group justify="flex-end">
            <Button type="button" size="xs" leftSection={<Plus size={16} />} onClick={() => setCreateOpen(true)}>
              {t("performance:reviewForms.create")}
            </Button>
          </Group>
        )}

        {formsQuery.isLoading ? (
          <Stack gap="sm">
            <Skeleton h={70} />
            <Skeleton h={70} />
          </Stack>
        ) : formsQuery.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(formsQuery.error, t("performance:genericError"))}
          </Text>
        ) : !formsQuery.data || formsQuery.data.length === 0 ? (
          <Text size="sm" c="dimmed" ta="center">
            {t("performance:reviewForms.empty")}
          </Text>
        ) : (
          <Stack gap="sm">
            {formsQuery.data.map((form) => (
              <Card key={form.id} withBorder padding="md">
                <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
                  <div>
                    <Text fw={600}>{form.title}</Text>
                    <Text size="xs" c="dimmed">
                      {t("performance:reviewForms.questionCount", { count: form.questions.length })}
                    </Text>
                  </div>
                  <Badge {...FORM_STATUS_BADGE[form.status]}>{t(`performance:reviewForms.status.${form.status}`)}</Badge>
                </Group>
                {canManage && form.status === "draft" && cycle.status !== "closed" && (
                  <Group justify="flex-end" mt="sm">
                    <Button
                      type="button"
                      size="xs"
                      variant="default"
                      loading={publishMutation.isPending && publishMutation.variables === form.id}
                      onClick={() => publishMutation.mutate(form.id)}
                    >
                      {t("performance:reviewForms.publish")}
                    </Button>
                  </Group>
                )}
              </Card>
            ))}
          </Stack>
        )}
      </Stack>

      {canCreate && (
        <CreateFormModal
          open={createOpen}
          reviewCycleId={cycle.id}
          onClose={() => setCreateOpen(false)}
          onCreated={invalidate}
        />
      )}
    </Drawer>
  );
}

function CreateFormModal({
  open,
  reviewCycleId,
  onClose,
  onCreated,
}: {
  open: boolean;
  reviewCycleId: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const { t } = useTranslation(["performance", "common"]);
  const [title, setTitle] = useState("");
  const [questions, setQuestions] = useState<QuestionRow[]>([{ text: "", weight: 0 }]);
  const [submitted, setSubmitted] = useState(false);

  const totalWeight = questions.reduce((sum, q) => sum + (Number.isFinite(q.weight) ? q.weight : 0), 0);
  const hasEmptyQuestion = questions.length === 0 || questions.some((q) => q.text.trim().length === 0);
  const titleMissing = title.trim().length === 0;
  const canSubmit = !titleMissing && !hasEmptyQuestion && totalWeight === 100;

  const mutation = useMutation({
    mutationFn: () =>
      createReviewForm({
        reviewCycleId,
        title: title.trim(),
        questions: questions.map((q) => ({ text: q.text.trim(), weight: q.weight })),
      }),
    onSuccess: () => {
      onCreated();
      toast({ variant: "success", title: t("common:success"), description: t("performance:reviewForms.created") });
      close();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: reviewFormErrorMessage(err, t) }),
  });

  function close() {
    setTitle("");
    setQuestions([{ text: "", weight: 0 }]);
    setSubmitted(false);
    onClose();
  }

  function updateQuestion(index: number, patch: Partial<QuestionRow>) {
    setQuestions((prev) => prev.map((q, i) => (i === index ? { ...q, ...patch } : q)));
  }

  function addQuestion() {
    setQuestions((prev) => [...prev, { text: "", weight: 0 }]);
  }

  function removeQuestion(index: number) {
    setQuestions((prev) => prev.filter((_, i) => i !== index));
  }

  function submit() {
    setSubmitted(true);
    if (!canSubmit) return;
    mutation.mutate();
  }

  return (
    <Modal opened={open} onClose={close} title={t("performance:reviewForms.createTitle")} size="lg">
      <Stack gap="md">
        <TextInput
          data-autofocus
          label={t("performance:reviewForms.fields.title")}
          withAsterisk
          value={title}
          onChange={(event) => setTitle(event.currentTarget.value)}
          error={submitted && titleMissing ? t("common:required") : undefined}
        />

        <Stack gap="sm">
          <Text size="sm" fw={500}>
            {t("performance:reviewForms.fields.questions")}
          </Text>
          {questions.map((question, index) => (
            <Group key={index} align="flex-start" gap="sm" wrap="nowrap">
              <TextInput
                className="flex-1"
                placeholder={t("performance:reviewForms.fields.questionText")}
                value={question.text}
                onChange={(event) => updateQuestion(index, { text: event.currentTarget.value })}
                error={submitted && question.text.trim().length === 0 ? t("common:required") : undefined}
              />
              <NumberInput
                w={100}
                min={0}
                max={100}
                suffix="%"
                value={question.weight}
                onChange={(value) => updateQuestion(index, { weight: typeof value === "number" ? value : 0 })}
              />
              <Button
                type="button"
                variant="subtle"
                color="red"
                aria-label={t("performance:reviewForms.removeQuestion")}
                onClick={() => removeQuestion(index)}
                disabled={questions.length <= 1}
              >
                <Trash2 size={16} />
              </Button>
            </Group>
          ))}
          <Group justify="space-between" align="center">
            <Button type="button" variant="default" size="xs" leftSection={<Plus size={14} />} onClick={addQuestion}>
              {t("performance:reviewForms.addQuestion")}
            </Button>
            <Text size="sm" c={totalWeight === 100 ? "dimmed" : "red"}>
              {t("performance:reviewForms.totalWeight", { total: totalWeight })}
            </Text>
          </Group>
          {submitted && totalWeight !== 100 && (
            <Text size="xs" c="red">
              {t("performance:reviewForms.errors.weightsMustSumTo100")}
            </Text>
          )}
        </Stack>

        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={close} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={mutation.isPending} disabled={!canSubmit} onClick={submit}>
            {t("performance:reviewForms.createSubmit")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
