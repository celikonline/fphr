import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Navigate } from "react-router";
import { Card, Stepper, Text, Title } from "@mantine/core";
import { Rocket } from "lucide-react";
import CalendarLocationStep from "@/components/setup/calendar-location-step";
import CompanyStep from "@/components/setup/company-step";
import ModulesUsersStep from "@/components/setup/modules-users-step";
import StructureStep from "@/components/setup/structure-step";
import { isSetupPending } from "@/components/setup-guard";
import { useImpersonatorId } from "@/hooks/use-access-setting";
import { useAuthStore } from "@/store/auth.store";

const STEP_KEYS = ["company", "calendar", "structure", "modules"] as const;
const LAST_STEP = STEP_KEYS.length - 1;

/**
 * First-run setup wizard for a newly created company. Mandatory for the Owner (see SetupGuard): every route redirects here
 * until the last step marks the tenant's setup as completed. Step 1 (company details) must be saved; the others can be skipped.
 */
export default function SetupWizardPage() {
  const { t } = useTranslation(["setup"]);
  const user = useAuthStore((state) => state.user);
  const impersonatorId = useImpersonatorId();
  const [active, setActive] = useState(0);

  // Nothing to set up (already finished, or not an owner): the wizard isn't a place to linger.
  if (!isSetupPending(user, impersonatorId)) {
    return <Navigate to="/app" replace />;
  }

  const next = () => setActive((step) => Math.min(step + 1, LAST_STEP));
  const back = () => setActive((step) => Math.max(step - 1, 0));

  return (
    <div className="max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Rocket size={24} /> {t("setup:title")}
        </Title>
        <Text c="dimmed">{t("setup:subtitle", { company: user?.tenant?.name ?? "" })}</Text>
      </div>

      <Stepper active={active} allowNextStepsSelect={false} size="sm">
        {STEP_KEYS.map((key) => (
          <Stepper.Step key={key} label={t(`setup:steps.${key}`)} />
        ))}
      </Stepper>

      <Card withBorder padding="lg">
        {active === 0 && <CompanyStep onContinue={next} />}
        {active === 1 && <CalendarLocationStep onContinue={next} onBack={back} />}
        {active === 2 && <StructureStep onContinue={next} onBack={back} />}
        {active === 3 && <ModulesUsersStep onBack={back} />}
      </Card>
    </div>
  );
}
