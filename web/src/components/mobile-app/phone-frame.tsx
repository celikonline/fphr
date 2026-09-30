import type { CSSProperties } from "react";
import classes from "./mobile-app.module.css";

export type PhoneFrameVariant = "hero-center" | "hero-left" | "hero-right" | "showcase";

const VARIANT_CLASSES: Record<PhoneFrameVariant, string> = {
  "hero-center": `${classes.phoneHeroCenter}`,
  "hero-left": `${classes.phoneHeroSide} ${classes.phoneHeroLeft}`,
  "hero-right": `${classes.phoneHeroSide} ${classes.phoneHeroRight}`,
  showcase: classes.phoneShowcase,
};

interface PhoneFrameProps {
  src: string;
  alt: string;
  variant: PhoneFrameVariant;
  /** Eager-loads the image; use for the phone that is visible above the fold on first paint. */
  priority?: boolean;
  /** Entrance stagger for the hero fan (ms). Ignored for the "showcase" variant. */
  animationDelayMs?: number;
}

/** Dark phone chrome around a 540x1170 app screenshot, shared by the hero fan and the feature showcase. */
export function PhoneFrame({ src, alt, variant, priority, animationDelayMs }: PhoneFrameProps) {
  return (
    <div
      className={`${classes.phoneFrame} ${VARIANT_CLASSES[variant]}`}
      style={
        animationDelayMs !== undefined
          ? ({ "--phone-delay": `${animationDelayMs}ms` } as CSSProperties)
          : undefined
      }
    >
      <img
        src={src}
        alt={alt}
        width={540}
        height={1170}
        loading={priority ? "eager" : "lazy"}
        className={classes.phoneScreen}
      />
    </div>
  );
}
