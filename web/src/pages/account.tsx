import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { LifeBuoy, UserCircle } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useUIStore, type Theme } from "@/store/ui.store";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

const LANGUAGES = [
  { code: "tr", label: "Türkçe" },
  { code: "en", label: "English" },
  { code: "de", label: "Deutsch" },
];

const THEMES: Theme[] = ["light", "dark", "system"];

function ProfileRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-1 gap-1 py-3 sm:grid-cols-3 sm:gap-4">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-sm sm:col-span-2">{children}</dd>
    </div>
  );
}

/** Kendi hesabım: profile basics from /identity/me plus client-side language and theme preferences. */
export default function AccountPage() {
  const { t, i18n } = useTranslation(["account", "common", "navigation"]);
  const user = useAuthStore((state) => state.user);
  const theme = useUIStore((state) => state.theme);
  const setTheme = useUIStore((state) => state.setTheme);

  const initials = (user?.name || user?.email || "?")
    .split(" ")
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
  const currentLanguage = LANGUAGES.find((lang) => i18n.resolvedLanguage?.startsWith(lang.code))?.code ?? "tr";

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <UserCircle className="h-6 w-6" /> {t("account:title")}
        </h1>
        <p className="text-muted-foreground">{t("account:subtitle")}</p>
      </div>

      <Card>
        <CardHeader className="flex flex-row items-center gap-4 space-y-0">
          <Avatar className="h-14 w-14">
            <AvatarFallback className="text-lg">{initials}</AvatarFallback>
          </Avatar>
          <div className="min-w-0">
            <CardTitle className="truncate">{user?.name || user?.email}</CardTitle>
            <CardDescription className="truncate">{user?.email}</CardDescription>
          </div>
        </CardHeader>
        <CardContent>
          <dl className="divide-y divide-border">
            <ProfileRow label={t("account:firstName")}>{user?.firstName || "—"}</ProfileRow>
            <ProfileRow label={t("account:lastName")}>{user?.lastName || "—"}</ProfileRow>
            <ProfileRow label={t("account:email")}>{user?.email ?? "—"}</ProfileRow>
            <ProfileRow label={t("account:company")}>{user?.tenant?.name ?? "—"}</ProfileRow>
            <ProfileRow label={t("account:roles")}>
              {user && user.roles.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {user.roles.map((role) => (
                    <Badge key={role} variant="secondary">
                      {t(`account:roleNames.${role}`, role)}
                    </Badge>
                  ))}
                </div>
              ) : (
                "—"
              )}
            </ProfileRow>
            <ProfileRow label={t("account:mfa")}>
              {user?.mfaEnabled ? t("account:mfaEnabled") : t("account:mfaDisabled")}
            </ProfileRow>
            {user?.employeeId && (
              <ProfileRow label={t("account:employeeCard")}>
                <Link to={`/app/employees/${user.employeeId}`} className="text-primary hover:underline">
                  {t("account:viewEmployeeCard")}
                </Link>
              </ProfileRow>
            )}
          </dl>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("account:preferences")}</CardTitle>
          <CardDescription>{t("account:preferencesHint")}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-6">
          <div className="max-w-xs space-y-2">
            <Label htmlFor="account-language">{t("common:language")}</Label>
            <Select value={currentLanguage} onValueChange={(code) => i18n.changeLanguage(code)}>
              <SelectTrigger id="account-language">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {LANGUAGES.map((lang) => (
                  <SelectItem key={lang.code} value={lang.code}>
                    {lang.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label>{t("common:theme")}</Label>
            <RadioGroup value={theme} onValueChange={(value) => setTheme(value as Theme)} className="flex flex-wrap gap-6">
              {THEMES.map((value) => (
                <div key={value} className="flex items-center gap-2">
                  <RadioGroupItem id={`theme-${value}`} value={value} />
                  <Label htmlFor={`theme-${value}`} className="font-normal">
                    {t(`common:theme${value.charAt(0).toUpperCase()}${value.slice(1)}`)}
                  </Label>
                </div>
              ))}
            </RadioGroup>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="flex flex-wrap items-center justify-between gap-3 p-6">
          <div>
            <p className="font-medium">{t("navigation:nav.support")}</p>
            <p className="text-sm text-muted-foreground">{t("account:supportHint")}</p>
          </div>
          <Button asChild variant="outline">
            <Link to="/app/support">
              <LifeBuoy className="mr-2 h-4 w-4" /> {t("navigation:nav.support")}
            </Link>
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
