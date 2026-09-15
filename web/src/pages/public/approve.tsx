import { useState } from "react";
import { useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Clock } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { getApiProblem } from "@/lib/api-error";
import { decideByToken, type DecideByTokenPayload } from "@/services/workflow.service";

type Result = "success" | "expired" | "alreadyDecided" | "error";

function CenteredCard({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/40 px-4 py-12">
      <Card className="w-full max-w-md">{children}</Card>
    </div>
  );
}

function ResultMessage({ result }: { result: Result }) {
  const { t } = useTranslation(["workflow"]);
  const config: Record<Result, { icon: typeof CheckCircle2; className: string; textKey: string }> = {
    success: { icon: CheckCircle2, className: "text-emerald-600", textKey: "workflow:publicDecision.success" },
    expired: { icon: Clock, className: "text-amber-500", textKey: "workflow:publicDecision.expired" },
    alreadyDecided: { icon: AlertTriangle, className: "text-amber-500", textKey: "workflow:publicDecision.alreadyDecided" },
    error: { icon: AlertTriangle, className: "text-destructive", textKey: "workflow:publicDecision.error" },
  };
  const { icon: Icon, className, textKey } = config[result];

  return (
    <CenteredCard>
      <CardHeader className="items-center text-center">
        <Icon className={`mb-2 h-10 w-10 ${className}`} />
        <CardTitle>{t("workflow:publicDecision.title")}</CardTitle>
        <CardDescription>{t(textKey)}</CardDescription>
      </CardHeader>
    </CenteredCard>
  );
}

export default function ApproveTokenPage() {
  const { t } = useTranslation(["workflow", "common"]);
  const { token = "" } = useParams();
  const [comment, setComment] = useState("");
  const [result, setResult] = useState<Result | null>(null);

  const mutation = useMutation({
    mutationFn: (outcome: DecideByTokenPayload["outcome"]) =>
      decideByToken({ token, outcome, comment: comment.trim() || undefined }),
    onSuccess: () => setResult("success"),
    onError: (error) => {
      const code = getApiProblem(error)?.code;
      if (code === "workflow.token.expired") setResult("expired");
      else if (code === "workflow.token.already_used") setResult("alreadyDecided");
      else setResult("error");
    },
  });

  if (!token) return <ResultMessage result="error" />;
  if (result) return <ResultMessage result={result} />;

  return (
    <CenteredCard>
      <CardHeader>
        <CardTitle>{t("workflow:publicDecision.title")}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div>
          <Label htmlFor="decision-comment">{t("workflow:publicDecision.comment")}</Label>
          <Textarea
            id="decision-comment"
            placeholder={t("workflow:publicDecision.commentPlaceholder")}
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            disabled={mutation.isPending}
          />
        </div>
        <div className="flex gap-2">
          <Button
            type="button"
            variant="destructive"
            className="flex-1"
            onClick={() => mutation.mutate("rejected")}
            disabled={mutation.isPending}
          >
            {t("workflow:publicDecision.reject")}
          </Button>
          <Button type="button" className="flex-1" onClick={() => mutation.mutate("approved")} disabled={mutation.isPending}>
            {t("workflow:publicDecision.approve")}
          </Button>
        </div>
      </CardContent>
    </CenteredCard>
  );
}
