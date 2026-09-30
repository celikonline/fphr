import React, { useState } from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useTranslation } from "react-i18next";
import { GlassCard } from "@/components/ui";
import type { AnnouncementFeedItem } from "@/services/HomeFeedService";
import { fontFor, palette, radius, text, tones } from "@/theme/theme";
import { toLocaleUpper } from "@/lib/formatters";
import { plainTextPreview } from "./homeSections";

type Props = {
  announcement: AnnouncementFeedItem;
};

const COLLAPSED_LINES = 3;
/** Roughly three lines on a phone; shorter bodies need no "Devamını oku". */
const EXPANDABLE_LENGTH = 140;

/**
 * "Duyurular" card: "YENİ" pill while unread, title, plain-text body. There is no announcement
 * detail screen on mobile, so "Devamını oku" expands the body in place.
 */
export function AnnouncementCard({ announcement }: Props) {
  const { t } = useTranslation("home");
  const [expanded, setExpanded] = useState(false);
  const body = plainTextPreview(announcement.content);
  const expandable = body.length > EXPANDABLE_LENGTH;

  return (
    <GlassCard style={styles.card}>
      {!announcement.isRead ? (
        <View style={styles.newPill}>
          <Text style={styles.newText}>{toLocaleUpper(t("announcements.new"))}</Text>
        </View>
      ) : null}
      <Text style={styles.title}>{announcement.title}</Text>
      {body.length > 0 ? (
        <Text style={styles.body} numberOfLines={expanded ? undefined : COLLAPSED_LINES}>
          {body}
        </Text>
      ) : null}
      {expandable ? (
        <TouchableOpacity
          onPress={() => setExpanded((value) => !value)}
          accessibilityRole="button"
          accessibilityLabel={expanded ? t("announcements.showLess") : t("announcements.readMore")}
          hitSlop={{ top: 10, bottom: 10, left: 4, right: 4 }}
        >
          <Text style={styles.more}>
            {expanded ? t("announcements.showLess") : t("announcements.readMore")}
          </Text>
        </TouchableOpacity>
      ) : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 12,
  },
  newPill: {
    alignSelf: "flex-start",
    marginBottom: 8,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: radius.pill,
    backgroundColor: tones.info.bg,
  },
  newText: {
    fontSize: 11,
    letterSpacing: 0.7,
    color: tones.info.fg,
    ...fontFor(800),
  },
  title: {
    fontSize: 15,
    lineHeight: 20,
    color: palette.white,
    ...fontFor(800),
  },
  body: {
    ...text.bodyMuted,
    marginTop: 6,
    fontSize: 13.5,
    lineHeight: 21,
    ...fontFor(500),
  },
  more: {
    marginTop: 8,
    fontSize: 13,
    color: palette.brandText,
    ...fontFor(700),
  },
});
