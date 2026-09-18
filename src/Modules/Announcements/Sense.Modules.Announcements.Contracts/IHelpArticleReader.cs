namespace Sense.Modules.Announcements.Contracts;

/// <summary>Yardım Merkezi makalesinin diğer modüllere (Faz 7: Ai RAG kaynağı) açılan düz görünümü.</summary>
public sealed record HelpArticleContentDto(
    string Slug, string Category, string TitleTr, string TitleEn, string BodyTr, string BodyEn, IReadOnlyList<string> RelatedRoutes);

/// <summary>
/// Yardım Merkezi makalelerini (content-as-code: repo içi Markdown, DB tablosu değil) cross-module okuma portu.
/// Roadmap #102: Faz 7'de Ai modülü bu makaleleri chunk'layıp "SenseİK Yardım Merkezi" kaynağı olarak
/// search_knowledge aracında kullanır. Uygulaması Sense.Modules.Announcements.Application'da (HelpArticleCatalog).
/// </summary>
public interface IHelpArticleReader
{
    Task<IReadOnlyList<HelpArticleContentDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
