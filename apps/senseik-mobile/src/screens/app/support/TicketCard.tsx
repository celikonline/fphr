import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { GlassCard, StatusPill } from "@/components/ui";
import type { SupportTicket } from "@/services/SupportService";
import { formatDateTime as formatLocaleDateTime } from "@/lib/formatters";
import { text } from "@/theme/theme";
import {
  normalizeTicketStatus,
  supportTicketStatusLabel,
  supportTicketStatusTone,
} from "./supportTicketStatus";

function formatDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return formatLocaleDateTime(date, {
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

type Props = {
  ticket: SupportTicket;
};

/** One "Taleplerim" row: subject + status pill, created date, and resolution if answered. */
export function TicketCard({ ticket }: Props) {
  const { t } = useTranslation("support");
  const status = normalizeTicketStatus(ticket.status);

  return (
    <GlassCard style={styles.card}>
      <View style={styles.header}>
        <Text style={styles.subject}>{ticket.subject}</Text>
        <StatusPill
          label={supportTicketStatusLabel(status)}
          tone={supportTicketStatusTone(status)}
        />
      </View>
      <Text style={styles.subtext}>{formatDateTime(ticket.createdAt)}</Text>
      {ticket.resolution ? (
        <Text style={styles.resolution}>
          {t("tickets.resolution", { resolution: ticket.resolution })}
        </Text>
      ) : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  subject: {
    ...text.heading,
    flex: 1,
  },
  subtext: {
    ...text.caption,
    marginTop: 6,
  },
  resolution: {
    ...text.bodyMuted,
    marginTop: 8,
  },
});
