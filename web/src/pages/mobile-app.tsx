import { useTranslation } from "react-i18next";
import { Card, SimpleGrid, Text, ThemeIcon, Title } from "@mantine/core";
import { FeatureShowcase } from "@/components/mobile-app/feature-showcase";
import { PhoneFrame } from "@/components/mobile-app/phone-frame";
import { StoreButton } from "@/components/mobile-app/store-button";
import { storeUrl } from "@/components/mobile-app/store-url";
import classes from "@/components/mobile-app/mobile-app.module.css";

const HOW_TO_START_STEPS = ["download", "login", "notifications"] as const;

function HeroPhones() {
  const { t } = useTranslation(["mobileApp"]);
  return (
    <div className="relative mx-auto flex max-w-full items-end justify-center lg:mx-0">
      <div className="z-0 hidden lg:-mr-20 lg:block">
        <PhoneFrame
          src="/images/mobile-app/clock-in.jpg"
          alt={t("mobileApp:hero.phoneAlt.clockIn")}
          variant="hero-left"
          animationDelayMs={0}
        />
      </div>
      <div className="z-10 min-w-0">
        <PhoneFrame
          src="/images/mobile-app/home.jpg"
          alt={t("mobileApp:hero.phoneAlt.home")}
          variant="hero-center"
          priority
          animationDelayMs={80}
        />
      </div>
      <div className="z-0 hidden lg:-ml-20 lg:block">
        <PhoneFrame
          src="/images/mobile-app/approvals.jpg"
          alt={t("mobileApp:hero.phoneAlt.approvals")}
          variant="hero-right"
          animationDelayMs={160}
        />
      </div>
    </div>
  );
}

function HeroSection() {
  const { t } = useTranslation(["mobileApp"]);
  const iosUrl = storeUrl(import.meta.env.VITE_IOS_APP_URL);
  const androidUrl = storeUrl(import.meta.env.VITE_ANDROID_APP_URL);

  return (
    <div
      className={`${classes.hero} grid gap-8 p-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] lg:p-14`}
    >
      <div aria-hidden="true" className={classes.heroGhostShape} />
      <div className="relative z-10 flex flex-col justify-center gap-4">
        <span className={classes.heroEyebrow}>{t("mobileApp:hero.eyebrow")}</span>
        <Title order={1} className={classes.heroTitle}>
          {t("mobileApp:hero.title")}
        </Title>
        <Text className={classes.heroDescription}>{t("mobileApp:hero.description")}</Text>
        <div className="flex flex-wrap gap-3 pt-2">
          <StoreButton store="ios" url={iosUrl} />
          <StoreButton store="android" url={androidUrl} />
        </div>
        <Text size="sm" className={classes.heroNote}>
          {t("mobileApp:hero.note")}
        </Text>
      </div>
      <HeroPhones />
    </div>
  );
}

function HowToStartSection() {
  const { t } = useTranslation(["mobileApp"]);
  return (
    <div>
      <Title order={2} size="h3" mb="md">
        {t("mobileApp:howToStart.heading")}
      </Title>
      <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
        {HOW_TO_START_STEPS.map((step, index) => (
          <Card key={step} withBorder padding="lg">
            <ThemeIcon variant="light" radius="xl" size={36} mb="sm">
              {index + 1}
            </ThemeIcon>
            <Title order={4} mb={4}>
              {t(`mobileApp:howToStart.steps.${step}.title`)}
            </Title>
            <Text size="sm" c="dimmed">
              {t(`mobileApp:howToStart.steps.${step}.body`)}
            </Text>
          </Card>
        ))}
      </SimpleGrid>
    </div>
  );
}

export default function MobileAppPage() {
  const { t } = useTranslation(["mobileApp"]);

  return (
    <div className="max-w-6xl space-y-10 p-6 md:p-10">
      <HeroSection />

      <div>
        <Title order={2} size="h3" mb="md">
          {t("mobileApp:featuresHeading")}
        </Title>
        <FeatureShowcase />
      </div>

      <HowToStartSection />
    </div>
  );
}
