import React from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { GlassCard, TextField } from "@/components/ui";
import type { HelpArticleSummary } from "@/services/SupportService";
import { palette, text } from "@/theme/theme";

type ArticleView = { title: string; body: string } | null;

type Props = {
  term: string;
  onTermChange: (value: string) => void;
  articles: HelpArticleSummary[];
  articlesLoading: boolean;
  selectedSlug: string | null;
  articleLoading: boolean;
  articleBody: ArticleView;
  onOpenArticle: (slug: string) => void;
  onCloseArticle: () => void;
};

/** "Yardım Merkezi": search + article list, or the selected article's body. */
export function HelpCenterCard({
  term,
  onTermChange,
  articles,
  articlesLoading,
  selectedSlug,
  articleLoading,
  articleBody,
  onOpenArticle,
  onCloseArticle,
}: Props) {
  return (
    <GlassCard style={styles.card}>
      {selectedSlug ? (
        <>
          <TouchableOpacity
            style={styles.backRow}
            onPress={onCloseArticle}
            accessibilityRole="button"
            accessibilityLabel="Listeye dön"
          >
            <Ionicons name="chevron-back" size={16} color={palette.brandText} />
            <Text style={styles.linkText}>Listeye dön</Text>
          </TouchableOpacity>
          {articleLoading ? (
            <ActivityIndicator style={styles.loading} color={palette.white} />
          ) : articleBody ? (
            <>
              <Text style={styles.articleTitle}>{articleBody.title}</Text>
              <Text style={styles.articleBody}>{articleBody.body}</Text>
            </>
          ) : null}
        </>
      ) : (
        <>
          <TextField
            label="Ara"
            placeholder="Ne aramıştınız? Örn. veri aktarımı"
            value={term}
            onChangeText={onTermChange}
            autoCapitalize="none"
          />
          {articlesLoading ? (
            <ActivityIndicator style={styles.loading} color={palette.white} />
          ) : articles.length === 0 ? (
            <Text style={styles.emptyText}>Bu aramayla eşleşen bir makale bulunamadı.</Text>
          ) : (
            articles.map((article, index) => (
              <TouchableOpacity
                key={article.slug}
                style={[styles.articleRow, index === 0 && styles.articleRowFirst]}
                onPress={() => onOpenArticle(article.slug)}
                accessibilityRole="button"
                accessibilityLabel={article.title}
              >
                <Text style={styles.articleRowText}>{article.title}</Text>
                <Ionicons name="chevron-forward" size={18} color={palette.textFaint} />
              </TouchableOpacity>
            ))
          )}
        </>
      )}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 16,
  },
  loading: {
    marginTop: 16,
  },
  emptyText: {
    ...text.bodyMuted,
    marginTop: 16,
  },
  articleRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingVertical: 12,
    marginTop: 8,
    borderTopWidth: 1,
    borderTopColor: palette.line,
  },
  articleRowFirst: {
    marginTop: 14,
  },
  articleRowText: {
    ...text.body,
    flex: 1,
    marginRight: 8,
  },
  backRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  linkText: {
    ...text.bodyMuted,
    color: palette.brandText,
  },
  articleTitle: {
    ...text.heading,
    marginTop: 14,
  },
  articleBody: {
    ...text.bodyMuted,
    marginTop: 8,
  },
});
