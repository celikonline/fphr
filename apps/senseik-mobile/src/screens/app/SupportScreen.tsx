import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet } from "react-native";
import { EmptyState, Screen, ScreenHeader, SectionHeader } from "@/components/ui";
import {
  supportService,
  type HelpArticleSummary,
  type SupportTicket,
  type SupportTicketCategory,
} from "@/services/SupportService";
import { palette } from "@/theme/theme";
import { HelpCenterCard } from "./support/HelpCenterCard";
import { NewTicketCard } from "./support/NewTicketCard";
import { TicketCard } from "./support/TicketCard";
import { normalizeTicketStatus, supportTicketStatusTone } from "./support/supportTicketStatus";

export { normalizeTicketStatus, supportTicketStatusTone };

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
    <Screen
      header={<ScreenHeader title="Destek" />}
      refreshing={refreshing}
      onRefresh={() => void onRefresh()}
    >
      <SectionHeader title="Yardım Merkezi" />
      <HelpCenterCard
        term={term}
        onTermChange={setTerm}
        articles={articles}
        articlesLoading={articlesLoading}
        selectedSlug={selectedSlug}
        articleLoading={articleLoading}
        articleBody={articleBody}
        onOpenArticle={(slug) => void openArticle(slug)}
        onCloseArticle={closeArticle}
      />

      <SectionHeader title="Desteğe bağlan" />
      <NewTicketCard
        category={category}
        onCategoryChange={setCategory}
        subject={subject}
        onSubjectChange={setSubject}
        body={body}
        onBodyChange={setBody}
        submitting={submitting}
        onSubmit={() => void submitTicket()}
      />

      <SectionHeader title="Taleplerim" />
      {ticketsLoading ? (
        <ActivityIndicator style={styles.loading} color={palette.white} />
      ) : tickets.length === 0 ? (
        <EmptyState icon="chatbox-ellipses-outline" title="Henüz bir destek talebiniz yok." />
      ) : (
        tickets.map((ticket) => <TicketCard key={ticket.id} ticket={ticket} />)
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  loading: {
    marginHorizontal: 20,
  },
});
