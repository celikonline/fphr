import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Text } from "@mantine/core";
import { MOBILE_APP_FEATURES, type MobileAppFeature } from "./features";
import { PhoneFrame } from "./phone-frame";
import classes from "./mobile-app.module.css";

interface FeatureButtonProps {
  feature: MobileAppFeature;
  selected: boolean;
  onSelect: () => void;
}

function FeatureButton({ feature, selected, onSelect }: FeatureButtonProps) {
  const { t } = useTranslation(["mobileApp"]);
  const Icon = feature.icon;
  const buttonClass = `${classes.featureButton} ${selected ? classes.featureButtonSelected : ""} flex w-full items-start gap-3 px-3 py-4`;

  return (
    <button type="button" aria-pressed={selected} onClick={onSelect} className={buttonClass}>
      <Icon className="mt-0.5 h-5 w-5 shrink-0" style={{ color: "var(--mantine-color-brand-6)" }} />
      <div>
        <Text fw={600} size="md">
          {t(`mobileApp:features.${feature.key}.title`)}
        </Text>
        <Text size="sm" c="dimmed">
          {t(`mobileApp:features.${feature.key}.body`)}
        </Text>
      </div>
    </button>
  );
}

/** "Uygulamada neler var": feature list on the left, the selected screen (in the hero's gece-gradient panel) on the right. */
export function FeatureShowcase() {
  const { t } = useTranslation(["mobileApp"]);
  const [selectedKey, setSelectedKey] = useState(MOBILE_APP_FEATURES[0].key);
  const selected =
    MOBILE_APP_FEATURES.find((feature) => feature.key === selectedKey) ?? MOBILE_APP_FEATURES[0];

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] items-center gap-10 md:grid-cols-[minmax(0,1fr)_auto]">
      <div className="order-2 flex max-w-xl min-w-0 flex-col gap-1 md:order-1">
        {MOBILE_APP_FEATURES.map((feature) => (
          <FeatureButton
            key={feature.key}
            feature={feature}
            selected={feature.key === selectedKey}
            onSelect={() => setSelectedKey(feature.key)}
          />
        ))}
      </div>
      <div className="order-1 flex min-w-0 justify-center md:order-2">
        <div className={classes.showcasePanel}>
          <div aria-hidden="true" className={classes.showcaseGhostShape} />
          <PhoneFrame
            key={selected.key}
            src={`/images/mobile-app/${selected.image}`}
            alt={t(`mobileApp:features.${selected.key}.alt`)}
            variant="showcase"
          />
        </div>
      </div>
    </div>
  );
}
