import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  SUPPORT_TICKET_CATEGORIES,
  TICKET_STATUS_LABELS,
  supportService,
  type HelpArticleSummary,
  type SupportTicket,
  type SupportTicketCategory
} from "@/services/SupportService";

const STATUS_COLORS: Record<SupportTicket["status"], string> = {
  open: colors.gray400,
  answered: colors.success,
  closed: colors.gray400
};

function formatDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("tr-TR", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });
}

/** "Hesabım > Destek": Yardım Merkezi araması + makale görünümü + Desteğe bağlan formu + Taleplerim (11.3.13). */
export default function SupportScreen() {
  const [refreshing, setRefreshing] = useState(false);

  // Yardım Merkezi
  const [term, setTerm] = useState("");
  const [articles, setArticles] = useState<HelpArticleSummary[]>([]);
  const [articlesLoading, setArticlesLoading] = useState(true);
  const [selectedSlug, setSelectedSlug] = useState<string | null>(null);
  const [articleBody, setArticleBody] = useState<{ title: string; body: string } | null>(null);
  const [articleLoading, setArticleLoading] = useState(false);

  // Desteğe bağlan
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [category, setCategory] = useState<SupportTicketCategory>("general");
  const [submitting, setSubmitting] = useState(false);

  // Taleplerim
  const [tickets, setTickets] = useState<SupportTicket[]>([]);
  const [ticketsLoading, setTicketsLoading] = useState(true);

  const loadArticles = useCallback(async (q?: string) => {
    setArticlesLoading(true);
    try {
      const result = await supportService.searchArticles(q);
      setArticles(result);
    } catch {
      setArticles([]);
    } finally {
      setArticlesLoading(false);
    }
  }, []);

  const loadTickets = useCallback(async () => {
    setTicketsLoading(true);
    try {
      const result = await supportService.listMyTickets();
      setTickets(result);
    } catch {
      setTickets([]);
    } finally {
      setTicketsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadArticles();
    void loadTickets();
  }, [loadArticles, loadTickets]);

  useEffect(() => {
    const handle = setTimeout(() => void loadArticles(term || undefined), 300);
    return () => clearTimeout(handle);
  }, [term, loadArticles]);

  async function onRefresh() {
    setRefreshing(true);
    await Promise.all([loadArticles(term || undefined), loadTickets()]);
    setRefreshing(false);
  }

  async function openArticle(slug: string) {
    setSelectedSlug(slug);
    setArticleLoading(true);
    try {
      const detail = await supportService.getArticle(slug);
      setArticleBody({ title: detail.title, body: detail.body });
    } catch {
      setArticleBody(null);
    } finally {
      setArticleLoading(false);
    }
  }

  function closeArticle() {
    setSelectedSlug(null);
    setArticleBody(null);
  }

  async function submitTicket() {
    if (subject.trim().length === 0 || body.trim().length === 0) {
      Alert.alert("Eksik bilgi", "Lütfen konu ve ayrıntı alanlarını doldurun.");
      return;
    }

    setSubmitting(true);
    try {
      await supportService.createTicket({ subject: subject.trim(), body: body.trim(), category });
      setSubject("");
      setBody("");
      setCategory("general");
      await loadTickets();
      Alert.alert("Gönderildi", "Talebiniz alındı. Destek ekibimiz size dönecek.");
    } catch {
      Alert.alert("Hata", "Talep gönderilemedi. Lütfen tekrar deneyin.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>Destek</Text>

      <Text style={styles.sectionTitle}>Yardım Merkezi</Text>
      <View style={styles.card}>
        {selectedSlug ? (
          <>
            <TouchableOpacity onPress={closeArticle}>
              <Text style={styles.linkText}>‹ Listeye dön</Text>
            </TouchableOpacity>
            {articleLoading ? (
              <ActivityIndicator style={{ marginTop: spacing.md }} color={colors.primary} />
            ) : articleBody ? (
              <>
                <Text style={[styles.cardValue, { marginTop: spacing.md }]}>{articleBody.title}</Text>
                <Text style={[styles.cardSubtext, { marginTop: spacing.sm }]}>{articleBody.body}</Text>
              </>
            ) : null}
          </>
        ) : (
          <>
            <TextInput
              style={styles.input}
              value={term}
              onChangeText={setTerm}
              placeholder="Ne aramıştınız? Örn. veri aktarımı"
              autoCapitalize="none"
            />
            {articlesLoading ? (
              <ActivityIndicator style={{ marginTop: spacing.md }} color={colors.primary} />
            ) : articles.length === 0 ? (
              <Text style={[styles.emptyText, { marginTop: spacing.md }]}>Bu aramayla eşleşen bir makale bulunamadı.</Text>
            ) : (
              articles.map((article) => (
                <TouchableOpacity key={article.slug} style={styles.articleRow} onPress={() => void openArticle(article.slug)}>
                  <Text style={styles.cardLabel}>{article.title}</Text>
                </TouchableOpacity>
              ))
            )}
          </>
        )}
      </View>

      <Text style={styles.sectionTitle}>Desteğe bağlan</Text>
      <View style={styles.card}>
        <Text style={styles.cardSubtext}>Aradığınızı bulamadınız mı? Kısa bir talep açın, ekibimiz size dönsün.</Text>

        <Text style={styles.fieldLabel}>Kategori</Text>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipRow}>
          {SUPPORT_TICKET_CATEGORIES.map((option) => {
            const selected = option.value === category;
            return (
              <TouchableOpacity
                key={option.value}
                style={[styles.chip, selected && styles.chipSelected]}
                onPress={() => setCategory(option.value)}
              >
                <Text style={[styles.chipText, selected && styles.chipTextSelected]}>{option.label}</Text>
              </TouchableOpacity>
            );
          })}
        </ScrollView>

        <Text style={styles.fieldLabel}>Konu</Text>
        <TextInput style={styles.input} value={subject} onChangeText={setSubject} placeholder="Talebinizi kısaca özetleyin" />

        <Text style={styles.fieldLabel}>Ayrıntı</Text>
        <TextInput
          style={[styles.input, styles.textArea]}
          value={body}
          onChangeText={setBody}
          placeholder="Ne yapmaya çalışıyordunuz, ne bekliyordunuz, ne oldu?"
          multiline
          numberOfLines={3}
        />

        <TouchableOpacity style={[styles.submitButton, submitting && styles.submitButtonDisabled]} onPress={() => void submitTicket()} disabled={submitting}>
          {submitting ? <ActivityIndicator color={colors.white} /> : <Text style={styles.submitButtonText}>Talep gönder</Text>}
        </TouchableOpacity>
      </View>

      <Text style={styles.sectionTitle}>Taleplerim</Text>
      {ticketsLoading ? (
        <ActivityIndicator color={colors.primary} />
      ) : tickets.length === 0 ? (
        <Text style={styles.emptyText}>Henüz bir destek talebiniz yok.</Text>
      ) : (
        tickets.map((ticket) => (
          <View key={ticket.id} style={styles.card}>
            <View style={styles.requestHeader}>
              <Text style={styles.cardLabel}>{ticket.subject}</Text>
              <View style={[styles.statusBadge, { backgroundColor: STATUS_COLORS[ticket.status] }]}>
                <Text style={styles.statusBadgeText}>{TICKET_STATUS_LABELS[ticket.status]}</Text>
              </View>
            </View>
            <Text style={styles.cardSubtext}>{formatDateTime(ticket.createdAt)}</Text>
            {ticket.resolution ? <Text style={[styles.cardSubtext, { marginTop: spacing.xs }]}>Yanıt: {ticket.resolution}</Text> : null}
          </View>
        ))
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm
  },
  cardLabel: {
    ...typography.bodySm,
    color: colors.textSecondary
  },
  cardValue: {
    ...typography.h4,
    color: colors.text
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary
  },
  linkText: {
    ...typography.bodySm,
    color: colors.primary
  },
  articleRow: {
    paddingVertical: spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: colors.border
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs
  },
  chipRow: {
    flexDirection: "row"
  },
  chip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    marginRight: spacing.sm,
    backgroundColor: colors.gray50
  },
  chipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary
  },
  chipText: {
    ...typography.bodySm,
    color: colors.text
  },
  chipTextSelected: {
    color: colors.white
  },
  input: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    backgroundColor: colors.white
  },
  textArea: {
    minHeight: 72,
    textAlignVertical: "top"
  },
  submitButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.md,
    alignItems: "center",
    marginTop: spacing.lg
  },
  submitButtonDisabled: {
    opacity: 0.6
  },
  submitButtonText: {
    ...typography.button,
    color: colors.white
  },
  requestHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  statusBadge: {
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2
  },
  statusBadgeText: {
    ...typography.caption,
    color: colors.white
  }
});
