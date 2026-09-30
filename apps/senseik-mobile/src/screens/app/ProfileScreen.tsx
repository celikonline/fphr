import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { LogoutConfirmSheet } from "@/components/auth/LogoutConfirmSheet";
import {
  Avatar,
  EmptyState,
  ErrorBanner,
  PrimaryButton,
  Screen,
  ScreenHeader,
  SegmentedControl,
} from "@/components/ui";
import { profileService, type MyProfile } from "@/services/ProfileService";
import { palette, text } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import { ProfileInfoCard } from "./profile/ProfileInfoCard";
import { ProfileMenuCard, type ProfileMenuRoute } from "./profile/ProfileMenuCard";
import { SettingsCard } from "./profile/SettingsCard";
import { buildProfileGroups, type ProfileGroup } from "./profile/profileGroups";

type ProfileNavigationProp = NativeStackNavigationProp<AppStackParamList>;

export default function ProfileScreen() {
  const { t } = useTranslation("profile");
  const navigation = useNavigation<ProfileNavigationProp>();
  const [profile, setProfile] = useState<MyProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [activeGroupKey, setActiveGroupKey] = useState<ProfileGroup["key"] | null>(null);
  const [logoutConfirmVisible, setLogoutConfirmVisible] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    setNotFound(false);
    try {
      const result = await profileService.getMyProfile();
      setProfile(result);
      const nextGroups = buildProfileGroups(result);
      setActiveGroupKey((current) =>
        current && nextGroups.some((group) => group.key === current)
          ? current
          : (nextGroups[0]?.key ?? null)
      );
    } catch (err) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 404) {
        setNotFound(true);
      } else {
        setError(i18n.t("profile:loadError"));
      }
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void load().finally(() => setLoading(false));
  }, [load]);

  async function onRefresh() {
    setRefreshing(true);
    await load();
    setRefreshing(false);
  }

  function onMenuNavigate(route: ProfileMenuRoute) {
    navigation.navigate(route);
  }

  const header = <ScreenHeader title={t("title")} />;

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
      </Screen>
    );
  }

  const subtitle = profile
    ? [profile.titleName, profile.departmentName].filter(Boolean).join(" · ")
    : "";
  // Built at render (not stored at load) so the group/field labels follow a language switch.
  const groups: ProfileGroup[] = profile ? buildProfileGroups(profile) : [];
  const activeGroup = groups.find((group) => group.key === activeGroupKey) ?? groups[0];

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      {notFound ? (
        <EmptyState icon="person-outline" title={t("notFound")} />
      ) : profile ? (
        <>
          <View style={styles.hero}>
            <Avatar name={profile.displayName} self size={84} radius={24} />
            <Text style={styles.name}>{profile.displayName}</Text>
            {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
          </View>

          {groups.length > 0 && activeGroup ? (
            <>
              <View style={styles.segmented}>
                <SegmentedControl
                  options={groups.map((group) => ({ value: group.key, label: group.label }))}
                  value={activeGroup.key}
                  onChange={(key) => setActiveGroupKey(key)}
                />
              </View>
              <ProfileInfoCard fields={activeGroup.fields} />
            </>
          ) : null}
        </>
      ) : null}

      <ProfileMenuCard onNavigate={onMenuNavigate} />

      <SettingsCard />

      <View style={styles.logout}>
        <PrimaryButton
          label={t("home:menu.logout")}
          variant="danger"
          onPress={() => setLogoutConfirmVisible(true)}
        />
      </View>
      <LogoutConfirmSheet
        visible={logoutConfirmVisible}
        onClose={() => setLogoutConfirmVisible(false)}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  hero: {
    alignItems: "center",
    paddingHorizontal: 20,
    paddingTop: 12,
    paddingBottom: 20,
    gap: 4,
  },
  name: {
    ...text.title,
    marginTop: 10,
  },
  subtitle: {
    ...text.bodyMuted,
  },
  segmented: {
    paddingHorizontal: 20,
    marginBottom: 12,
  },
  logout: {
    marginHorizontal: 20,
    marginTop: 4,
  },
});
