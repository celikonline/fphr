import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import type { CompositeNavigationProp } from "@react-navigation/native";
import { useNavigation } from "@react-navigation/native";
import type { BottomTabNavigationProp } from "@react-navigation/bottom-tabs";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { profileService, type MyProfile } from "@/services/ProfileService";
import { leaveService, type LeaveRequest } from "@/services/LeaveService";
import { LeaveRequestListItem } from "@/components/leave/LeaveRequestListItem";
import type { AppStackParamList, MainTabsParamList } from "@/types/navigation";

/** Number of recent leave requests shown in the "Taleplerim" summary on the profile screen. */
const RECENT_LEAVE_REQUEST_COUNT = 5;

type ProfileNavigationProp = CompositeNavigationProp<
  BottomTabNavigationProp<MainTabsParamList, "Profile">,
  NativeStackNavigationProp<AppStackParamList>
>;

function formatDateOnly(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" });
}

interface ProfileField {
  label: string;
  value: string;
}

function buildFields(profile: MyProfile): ProfileField[] {
  const fields: ProfileField[] = [];
  if (profile.titleName) fields.push({ label: "Unvan", value: profile.titleName });
  if (profile.departmentName) fields.push({ label: "Birim", value: profile.departmentName });
  fields.push({
    label: "İşe giriş tarihi",
    value: profile.hireDate ? formatDateOnly(profile.hireDate) : "—",
  });
  fields.push({ label: "İş e-postası", value: profile.workEmail ?? "—" });
  const phone = profile.mobilePhone ?? profile.workPhone;
  if (phone) fields.push({ label: "Telefon", value: phone });
  return fields;
}

export default function ProfileScreen() {
  const navigation = useNavigation<ProfileNavigationProp>();
  const [profile, setProfile] = useState<MyProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [recentLeaveRequests, setRecentLeaveRequests] = useState<LeaveRequest[]>([]);
  const [leaveRequestsError, setLeaveRequestsError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    setNotFound(false);
    try {
      const result = await profileService.getMyProfile();
      setProfile(result);
    } catch (err) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 404) {
        setNotFound(true);
      } else {
        setError("Profil bilgileri yüklenemedi. Lütfen tekrar deneyin.");
      }
    }
  }, []);

  const loadRecentLeaveRequests = useCallback(async () => {
    setLeaveRequestsError(null);
    try {
      const requests = await leaveService.getMyRequests();
      const sorted = [...requests].sort(
        (a, b) => new Date(b.submittedAt).getTime() - new Date(a.submittedAt).getTime()
      );
      setRecentLeaveRequests(sorted.slice(0, RECENT_LEAVE_REQUEST_COUNT));
    } catch {
      setLeaveRequestsError("İzin talepleri yüklenemedi.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void Promise.all([load(), loadRecentLeaveRequests()]).finally(() => setLoading(false));
  }, [load, loadRecentLeaveRequests]);

  async function onRefresh() {
    setRefreshing(true);
    await Promise.all([load(), loadRecentLeaveRequests()]);
    setRefreshing(false);
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>Profilim</Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      {notFound ? (
        <Text style={styles.emptyText}>Çalışan kaydınız bulunmuyor.</Text>
      ) : profile ? (
        <View style={styles.card}>
          <Text style={styles.name}>{profile.displayName}</Text>
          {buildFields(profile).map((field) => (
            <View key={field.label} style={styles.fieldRow}>
              <Text style={styles.fieldLabel}>{field.label}</Text>
              <Text style={styles.fieldValue}>{field.value}</Text>
            </View>
          ))}
        </View>
      ) : null}

      <TouchableOpacity style={styles.supportRow} onPress={() => navigation.navigate("People")}>
        <Text style={styles.supportRowText}>Kişiler</Text>
        <Text style={styles.supportRowChevron}>›</Text>
      </TouchableOpacity>

      <TouchableOpacity style={styles.supportRow} onPress={() => navigation.navigate("Goals")}>
        <Text style={styles.supportRowText}>Hedeflerim</Text>
        <Text style={styles.supportRowChevron}>›</Text>
      </TouchableOpacity>

      <Text style={styles.sectionTitle}>Taleplerim</Text>
      {leaveRequestsError && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{leaveRequestsError}</Text>
        </View>
      )}
      {!leaveRequestsError && recentLeaveRequests.length === 0 ? (
        <Text style={styles.emptyText}>Henüz izin talebiniz yok.</Text>
      ) : (
        recentLeaveRequests.map((request) => (
          <LeaveRequestListItem
            key={request.id}
            request={request}
            onPress={() => navigation.navigate("Leave")}
          />
        ))
      )}

      <TouchableOpacity style={styles.supportRow} onPress={() => navigation.navigate("Support")}>
        <Text style={styles.supportRowText}>Destek</Text>
        <Text style={styles.supportRowChevron}>›</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
  },
  name: {
    ...typography.h4,
    color: colors.text,
    marginBottom: spacing.md,
  },
  fieldRow: {
    marginTop: spacing.sm,
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
  },
  fieldValue: {
    ...typography.body,
    color: colors.text,
    marginTop: 2,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  errorBanner: {
    backgroundColor: "#fdecea",
    borderRadius: 8,
    padding: spacing.md,
    marginBottom: spacing.md,
  },
  errorText: {
    ...typography.bodySm,
    color: colors.error,
  },
  supportRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginTop: spacing.lg,
  },
  supportRowText: {
    ...typography.body,
    color: colors.text,
  },
  supportRowChevron: {
    ...typography.h4,
    color: colors.textTertiary,
  },
});
