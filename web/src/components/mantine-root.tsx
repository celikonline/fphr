import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { MantineProvider } from "@mantine/core";
import { DatesProvider } from "@mantine/dates";
import { useColorScheme } from "@mantine/hooks";
import { Notifications } from "@mantine/notifications";
import dayjs from "dayjs";
import "dayjs/locale/tr";
import "dayjs/locale/de";
import customParseFormat from "dayjs/plugin/customParseFormat";
import { mantineCssVariablesResolver, mantineTheme } from "@/lib/mantine-theme";
import { useUIStore } from "@/store/ui.store";

dayjs.extend(customParseFormat);

/** Mantine providers: theme, color scheme from the UI store (light/dark/system), date locale from i18n, notifications. */
export function MantineRoot({ children }: { children: ReactNode }) {
  const theme = useUIStore((state) => state.theme);
  const systemScheme = useColorScheme();
  const { i18n } = useTranslation();
  const colorScheme = theme === "system" ? systemScheme : theme;
  const locale = (i18n.resolvedLanguage ?? i18n.language ?? "tr").split("-")[0];

  return (
    <MantineProvider theme={mantineTheme} cssVariablesResolver={mantineCssVariablesResolver} forceColorScheme={colorScheme}>
      <DatesProvider settings={{ locale, firstDayOfWeek: 1, weekendDays: [0, 6] }}>
        <Notifications position="top-right" limit={3} />
        {children}
      </DatesProvider>
    </MantineProvider>
  );
}
