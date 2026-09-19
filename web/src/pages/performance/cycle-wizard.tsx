import { useState } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import {
  Button,
  Card,
  Group,
  NumberInput,
  Select,
  Stack,
  Stepper,
  Switch,
  Text,
  TextInput,
  Textarea,
  Title,
} from "@mantine/core";
import { ClipboardCheck, Plus, Trash2 } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { DatePicker } from "@/components/ui/date-picker";
import {
  createReviewCycle,
  type CommentMode,
  type CompetencyInput,
  type EvaluatorRelation,
  type EvaluatorLabelMode,
  type GoalEntryBy,
  type QuestionInput,
  type RatingScale,
  type RelationConfig,
  type ReviewKind,
} from "@/services/performance.service";

const RELATION_ORDER: EvaluatorRelation[] = ["self", "manager", "subordinate", "peer"];
const WIZARD_STEPS = ["general", "competencies", "goalSettings", "access"] as const;
type WizardStepKey = (typeof WIZARD_STEPS)[number];

interface QuestionDraft {
  key: string;
  text: string;
  weight: string;
  commentMode: CommentMode | "";
}

interface CompetencyDraft {
  key: string;
  name: string;
  weight: string;
  questions: QuestionDraft[];
}

interface RelationDraft {
  relation: EvaluatorRelation;
  isEnabled: boolean;
  weight: string;
  minRequired: string;
}

function newQuestion(): QuestionDraft {
  return { key: crypto.randomUUID(), text: "", weight: "", commentMode: "" };
}

function newCompetency(): CompetencyDraft {
  return { key: crypto.randomUUID(), name: "", weight: "", questions: [newQuestion()] };
}

function defaultRelationDrafts(): RelationDraft[] {
  return RELATION_ORDER.map((relation) => ({
    relation,
    isEnabled: relation === "self" || relation === "manager",
    weight: "",
    minRequired: relation === "self" ? "1" : relation === "manager" ? "1" : "",
  }));
}

function toNumber(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

export default function CycleWizardPage() {
  const canCreate = usePermission("performance.cycle.create");
  if (!canCreate) return <NoAccess />;
  return <CycleWizard />;
}

function CycleWizard() {
  const { t } = useTranslation(["performance", "common"]);
  const navigate = useNavigate();
  const [active, setActive] = useState(0);

  const [name, setName] = useState("");
  const [periodStart, setPeriodStart] = useState("");
  const [periodEnd, setPeriodEnd] = useState("");
  const [kind, setKind] = useState<ReviewKind>("competency");
  const [ratingScale, setRatingScale] = useState<RatingScale>("zeroToHundred");
  const [defaultCommentMode, setDefaultCommentMode] = useState<CommentMode>("optional");
  const [evaluatorLabelMode, setEvaluatorLabelMode] = useState<EvaluatorLabelMode>("name");

  const [relations, setRelations] = useState<RelationDraft[]>(defaultRelationDrafts());
  const [competencies, setCompetencies] = useState<CompetencyDraft[]>([newCompetency()]);

  const [goalEntryBy, setGoalEntryBy] = useState<GoalEntryBy>("employee");
  const [competencyWeight, setCompetencyWeight] = useState("");

  const [employeeCanViewResults, setEmployeeCanViewResults] = useState(false);
  const [employeeCanDownloadPdf, setEmployeeCanDownloadPdf] = useState(false);

  const steps: WizardStepKey[] = kind === "competencyAndGoal" ? [...WIZARD_STEPS] : WIZARD_STEPS.filter((s) => s !== "goalSettings");

  const mutation = useMutation({
    mutationFn: async () => {
      const relationConfigs: RelationConfig[] = relations.map((r) => ({
        relation: r.relation,
        isEnabled: r.isEnabled,
        weight: r.isEnabled ? toNumber(r.weight) : null,
        minRequired: r.isEnabled ? toNumber(r.minRequired) : null,
      }));
      const competencyInputs: CompetencyInput[] = competencies.map((c) => ({
        name: c.name.trim(),
        weight: toNumber(c.weight),
        questions: c.questions.map(
          (q, index): QuestionInput => ({
            order: index + 1,
            text: q.text.trim(),
            weight: toNumber(q.weight),
            commentMode: q.commentMode || null,
          })
        ),
      }));
      return createReviewCycle({
        name: name.trim(),
        periodStart,
        periodEnd,
        kind,
        ratingScale,
        defaultCommentMode,
        evaluatorLabelMode,
        reportShowPhoto: false,
        employeeCanViewResults,
        employeeCanDownloadPdf,
        goalEntryBy: kind === "competencyAndGoal" ? goalEntryBy : "employee",
        competencyWeight: kind === "competencyAndGoal" ? toNumber(competencyWeight) : null,
        relationConfigs,
        competencies: competencyInputs,
      });
    },
    onSuccess: (id) => {
      toast({ variant: "success", title: t("common:success"), description: t("performance:wizard.created") });
      navigate(`/app/performance/cycles/${id}`);
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  const currentKey = steps[active];
  const isLast = active === steps.length - 1;
  const isFirst = active === 0;

  const canProceed = (() => {
    if (currentKey === "general") return name.trim().length > 0 && periodStart.length > 0 && periodEnd.length > 0;
    if (currentKey === "competencies") return competencies.length > 0 && competencies.every((c) => c.name.trim() && c.questions.every((q) => q.text.trim()));
    return true;
  })();

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <ClipboardCheck size={24} /> {t("performance:wizard.title")}
        </Title>
        <Text c="dimmed">{t("performance:wizard.subtitle")}</Text>
      </div>

      <Stepper active={active} size="sm" onStepClick={setActive} allowNextStepsSelect={false}>
        {steps.map((step) => (
          <Stepper.Step key={step} label={t(`performance:wizard.steps.${step}`)} />
        ))}
      </Stepper>

      <Card withBorder padding="lg">
        {currentKey === "general" && (
          <Stack gap="md">
            <TextInput label={t("performance:wizard.name")} withAsterisk value={name} onChange={(e) => setName(e.currentTarget.value)} maxLength={200} />
            <Group grow>
              <DatePicker value={periodStart} onChange={setPeriodStart} placeholder={t("performance:wizard.periodStart")} />
              <DatePicker value={periodEnd} onChange={setPeriodEnd} placeholder={t("performance:wizard.periodEnd")} min={periodStart || undefined} />
            </Group>
            <Select
              label={t("performance:wizard.kind")}
              value={kind}
              onChange={(v) => v && setKind(v as ReviewKind)}
              data={[
                { value: "competency", label: t("performance:kind.competency") },
                { value: "competencyAndGoal", label: t("performance:kind.competencyAndGoal") },
              ]}
              allowDeselect={false}
            />
            <Select
              label={t("performance:wizard.ratingScale")}
              value={ratingScale}
              onChange={(v) => v && setRatingScale(v as RatingScale)}
              data={[
                { value: "zeroToHundred", label: t("performance:ratingScale.zeroToHundred") },
                { value: "fivePointAgreement", label: t("performance:ratingScale.fivePointAgreement") },
              ]}
              allowDeselect={false}
            />
            <Select
              label={t("performance:wizard.defaultCommentMode")}
              value={defaultCommentMode}
              onChange={(v) => v && setDefaultCommentMode(v as CommentMode)}
              data={[
                { value: "optional", label: t("performance:commentMode.optional") },
                { value: "required", label: t("performance:commentMode.required") },
                { value: "disabled", label: t("performance:commentMode.disabled") },
              ]}
              allowDeselect={false}
            />
            <Select
              label={t("performance:wizard.evaluatorLabelMode")}
              description={t("performance:wizard.evaluatorLabelModeHint")}
              value={evaluatorLabelMode}
              onChange={(v) => v && setEvaluatorLabelMode(v as EvaluatorLabelMode)}
              data={[
                { value: "name", label: t("performance:evaluatorLabelMode.name") },
                { value: "categoryOnly", label: t("performance:evaluatorLabelMode.categoryOnly") },
              ]}
              allowDeselect={false}
            />
          </Stack>
        )}

        {currentKey === "competencies" && (
          <CompetenciesStep relations={relations} setRelations={setRelations} competencies={competencies} setCompetencies={setCompetencies} />
        )}

        {currentKey === "goalSettings" && (
          <Stack gap="md">
            <Select
              label={t("performance:wizard.goalEntryBy")}
              description={t("performance:wizard.goalEntryByHint")}
              value={goalEntryBy}
              onChange={(v) => v && setGoalEntryBy(v as GoalEntryBy)}
              data={[
                { value: "employee", label: t("performance:goalEntryBy.employee") },
                { value: "firstManager", label: t("performance:goalEntryBy.firstManager") },
              ]}
              allowDeselect={false}
            />
            <NumberInput
              label={t("performance:wizard.competencyWeight")}
              description={t("performance:wizard.competencyWeightHint")}
              value={competencyWeight}
              onChange={(v) => setCompetencyWeight(String(v ?? ""))}
              min={0}
              max={100}
              suffix="%"
            />
          </Stack>
        )}

        {currentKey === "access" && (
          <Stack gap="md">
            <Switch
              label={t("performance:wizard.employeeCanViewResults")}
              checked={employeeCanViewResults}
              onChange={(e) => setEmployeeCanViewResults(e.currentTarget.checked)}
            />
            <Switch
              label={t("performance:wizard.employeeCanDownloadPdf")}
              checked={employeeCanDownloadPdf}
              onChange={(e) => setEmployeeCanDownloadPdf(e.currentTarget.checked)}
            />
            <Title order={5} mt="sm">
              {t("performance:wizard.minEvaluators")}
            </Title>
            <Text size="sm" c="dimmed">
              {t("performance:wizard.minEvaluatorsHint")}
            </Text>
            <Stack gap="xs">
              {relations
                .filter((r) => r.isEnabled)
                .map((r) => (
                  <Group key={r.relation} justify="space-between">
                    <Text size="sm">{t(`performance:relation.${r.relation}`)}</Text>
                    {r.relation === "self" ? (
                      <Switch
                        checked={r.minRequired === "1"}
                        onChange={(e) =>
                          setRelations((prev) =>
                            prev.map((x) => (x.relation === r.relation ? { ...x, minRequired: e.currentTarget.checked ? "1" : "" } : x))
                          )
                        }
                      />
                    ) : (
                      <NumberInput
                        w={120}
                        min={1}
                        value={r.minRequired}
                        onChange={(v) =>
                          setRelations((prev) => prev.map((x) => (x.relation === r.relation ? { ...x, minRequired: String(v ?? "") } : x)))
                        }
                      />
                    )}
                  </Group>
                ))}
            </Stack>
          </Stack>
        )}
      </Card>

      <Group justify="space-between">
        <Button type="button" variant="default" onClick={() => (isFirst ? navigate("/app/performance/cycles") : setActive((a) => a - 1))}>
          {isFirst ? t("common:cancel") : t("common:back")}
        </Button>
        {isLast ? (
          <Button type="button" loading={mutation.isPending} disabled={!canProceed} onClick={() => mutation.mutate()}>
            {t("performance:wizard.finish")}
          </Button>
        ) : (
          <Button type="button" disabled={!canProceed} onClick={() => setActive((a) => a + 1)}>
            {t("performance:wizard.next")}
          </Button>
        )}
      </Group>
    </div>
  );
}

function CompetenciesStep({
  relations,
  setRelations,
  competencies,
  setCompetencies,
}: {
  relations: RelationDraft[];
  setRelations: React.Dispatch<React.SetStateAction<RelationDraft[]>>;
  competencies: CompetencyDraft[];
  setCompetencies: React.Dispatch<React.SetStateAction<CompetencyDraft[]>>;
}) {
  const { t } = useTranslation(["performance", "common"]);

  return (
    <Stack gap="xl">
      <div>
        <Title order={5} mb="xs">
          {t("performance:wizard.relationCategories")}
        </Title>
        <Stack gap="xs">
          {relations.map((r) => (
            <Group key={r.relation} justify="space-between" wrap="nowrap">
              <Switch
                label={t(`performance:relation.${r.relation}`)}
                checked={r.isEnabled}
                disabled={r.relation === "self" || r.relation === "manager"}
                onChange={(e) =>
                  setRelations((prev) => prev.map((x) => (x.relation === r.relation ? { ...x, isEnabled: e.currentTarget.checked } : x)))
                }
              />
              <NumberInput
                w={140}
                placeholder={t("performance:wizard.weightPlaceholder")}
                suffix="%"
                disabled={!r.isEnabled}
                value={r.weight}
                onChange={(v) => setRelations((prev) => prev.map((x) => (x.relation === r.relation ? { ...x, weight: String(v ?? "") } : x)))}
              />
            </Group>
          ))}
        </Stack>
        <Text size="xs" c="dimmed" mt={4}>
          {t("performance:wizard.selfManagerAlwaysOn")}
        </Text>
      </div>

      <div>
        <Group justify="space-between" mb="xs">
          <Title order={5}>{t("performance:wizard.competenciesTitle")}</Title>
          <Button type="button" size="xs" variant="default" leftSection={<Plus size={14} />} onClick={() => setCompetencies((prev) => [...prev, newCompetency()])}>
            {t("performance:wizard.addCompetency")}
          </Button>
        </Group>
        <Stack gap="md">
          {competencies.map((competency, index) => (
            <Card key={competency.key} withBorder padding="md">
              <Group justify="space-between" align="flex-end" mb="sm">
                <Group align="flex-end" grow style={{ flex: 1 }}>
                  <TextInput
                    label={t("performance:wizard.competencyName")}
                    value={competency.name}
                    onChange={(e) =>
                      setCompetencies((prev) => prev.map((c, i) => (i === index ? { ...c, name: e.currentTarget.value } : c)))
                    }
                  />
                  <NumberInput
                    label={t("performance:wizard.weight")}
                    suffix="%"
                    value={competency.weight}
                    onChange={(v) => setCompetencies((prev) => prev.map((c, i) => (i === index ? { ...c, weight: String(v ?? "") } : c)))}
                  />
                </Group>
                {competencies.length > 1 && (
                  <Button
                    type="button"
                    variant="subtle"
                    color="red"
                    size="xs"
                    px="xs"
                    onClick={() => setCompetencies((prev) => prev.filter((_, i) => i !== index))}
                  >
                    <Trash2 size={16} />
                  </Button>
                )}
              </Group>

              <Stack gap="xs">
                {competency.questions.map((question, qIndex) => (
                  <Group key={question.key} align="flex-start" wrap="nowrap">
                    <Textarea
                      style={{ flex: 1 }}
                      placeholder={t("performance:wizard.questionText")}
                      autosize
                      minRows={1}
                      value={question.text}
                      onChange={(e) =>
                        setCompetencies((prev) =>
                          prev.map((c, i) =>
                            i === index
                              ? { ...c, questions: c.questions.map((q, j) => (j === qIndex ? { ...q, text: e.currentTarget.value } : q)) }
                              : c
                          )
                        )
                      }
                    />
                    <NumberInput
                      w={100}
                      suffix="%"
                      placeholder={t("performance:wizard.weight")}
                      value={question.weight}
                      onChange={(v) =>
                        setCompetencies((prev) =>
                          prev.map((c, i) =>
                            i === index ? { ...c, questions: c.questions.map((q, j) => (j === qIndex ? { ...q, weight: String(v ?? "") } : q)) } : c
                          )
                        )
                      }
                    />
                    <Select
                      w={160}
                      placeholder={t("performance:wizard.commentModeDefault")}
                      value={question.commentMode}
                      onChange={(v) =>
                        setCompetencies((prev) =>
                          prev.map((c, i) =>
                            i === index
                              ? { ...c, questions: c.questions.map((q, j) => (j === qIndex ? { ...q, commentMode: (v as CommentMode) ?? "" } : q)) }
                              : c
                          )
                        )
                      }
                      data={[
                        { value: "optional", label: t("performance:commentMode.optional") },
                        { value: "required", label: t("performance:commentMode.required") },
                        { value: "disabled", label: t("performance:commentMode.disabled") },
                      ]}
                      clearable
                    />
                    {competency.questions.length > 1 && (
                      <Button
                        type="button"
                        variant="subtle"
                        color="red"
                        size="xs"
                        px="xs"
                        onClick={() =>
                          setCompetencies((prev) =>
                            prev.map((c, i) => (i === index ? { ...c, questions: c.questions.filter((_, j) => j !== qIndex) } : c))
                          )
                        }
                      >
                        <Trash2 size={16} />
                      </Button>
                    )}
                  </Group>
                ))}
                <Button
                  type="button"
                  size="xs"
                  variant="subtle"
                  leftSection={<Plus size={14} />}
                  onClick={() =>
                    setCompetencies((prev) => prev.map((c, i) => (i === index ? { ...c, questions: [...c.questions, newQuestion()] } : c)))
                  }
                >
                  {t("performance:wizard.addQuestion")}
                </Button>
              </Stack>
            </Card>
          ))}
        </Stack>
      </div>
    </Stack>
  );
}
