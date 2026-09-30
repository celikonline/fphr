using System.Globalization;
using Sense.Modules.Announcements.Contracts;

namespace Sense.Modules.Announcements.Application.Support;

/// <summary>Tek bir Yardım Merkezi makalesi (content-as-code: repo içi sabit içerik, DB tablosu değil).</summary>
public sealed record HelpArticle(
    string Slug,
    string Category,
    string TitleTr,
    string TitleEn,
    string BodyTr,
    string BodyEn,
    IReadOnlyList<string> RelatedRoutes,
    IReadOnlyList<string> Media);

/// <summary>
/// Yardım Merkezi içeriği kodda tutulur (sürüm = git, yayın = deploy, gözden geçirme = PR) — Kolay İK'nın Zendesk
/// benzeri editör/sürüm/yayın akışını DB tablosu + platform editörüyle yeniden yazmanın maliyeti bu fazda
/// gerekçelendirilemez; gerçek dev-dışı yazar ihtiyacı çıkarsa Faz 8'de DB tabanlı bir override katmanı eklenir,
/// içerik modeli aynı kalır. Faz 7'de Ai modülü IHelpArticleReader üzerinden aynı kataloğu RAG kaynağı yapar (#102).
/// İlk liste, analiz dokümanlarında en çok referans alınan Kolay İK destek makalelerini karşılıyor
/// (11.3.5 eki A1/A6, destek #1552348/#4008170/#6236761): veri aktarımı, izin devri, onay süreçleri, yöneticinin
/// çalışan adına izin eklemesi, erişim ayarları, destek erişimi (impersonation) onayı.
/// </summary>
public static class HelpArticleCatalog
{
    public static readonly IReadOnlyList<HelpArticle> Articles =
    [
        new(
            Slug: "veri-aktarimi-izin-gunleri",
            Category: "data_transfer",
            TitleTr: "Veri aktarımı: İzin bilgileri",
            TitleEn: "Data import: Leave balances",
            BodyTr: "Ayarlar > Veri aktar > İzin bilgileri'nden çalışanların devreden/kalan izin günlerini toplu " +
                    "olarak içe aktarabilirsiniz. Şablonu indirin, her satıra bir çalışan yazın, kaydedip yükleyin. " +
                    "Hatalı satırlar içe aktarılmadan önce ayrı listelenir; geri kalan satırlar yine de işlenir.",
            BodyEn: "Settings > Import data > Leave balances lets you bulk-import each employee's carried-over and " +
                    "remaining leave days. Download the template, fill one row per employee, then upload it. Rows " +
                    "with errors are listed separately before import; the rest are still processed.",
            RelatedRoutes: ["/admin/settings/import"],
            Media: []),
        new(
            Slug: "izin-devretme-secenekleri",
            Category: "leave_and_overtime",
            TitleTr: "İzin türü sihirbazı: Devretme seçenekleri",
            TitleEn: "Leave type wizard: Carry-over options",
            BodyTr: "Bir izin türü tanımlarken devir adımında üç seçenek vardır: Limitsiz Devretme (eksi bakiye de " +
                    "devreder), Limitli Devretme (bir üst sınır belirlersiniz) ve sıfırlama (devir yok, dönem sonunda " +
                    "kalan gün silinir). Seçim, o izin türünü kullanan tüm çalışanlar için geçerli olur.",
            BodyEn: "When defining a leave type, the carry-over step offers three options: unlimited carry-over " +
                    "(negative balances carry over too), limited carry-over (you set a cap) and reset (no carry-over, " +
                    "remaining days are cleared at period end). The choice applies to every employee on that leave type.",
            RelatedRoutes: ["/admin/settings/rules"],
            Media: []),
        new(
            Slug: "onay-surecleri-tasarimi",
            Category: "approvals",
            TitleTr: "Onay süreçleri nasıl tasarlanır?",
            TitleEn: "How to design approval flows",
            BodyTr: "Ayarlar > Kurallar > Onay Süreçleri'nden her talep türü (izin, avans, harcama, vize belgesi) için " +
                    "ayrı bir onay zinciri kurabilirsiniz: sırayla onaylayacak roller, paralel onay ve otomatik " +
                    "onay/red koşulları. Bir çalışana atanmış onaycı yoksa talep bir üst role yükselir.",
            BodyEn: "Settings > Rules > Approval flows lets you build a separate approval chain per request type " +
                    "(leave, advance, expense, work-permit document): sequential approver roles, parallel approval " +
                    "and automatic approve/reject conditions. If an employee has no assigned approver, the request " +
                    "escalates to the next role up.",
            RelatedRoutes: ["/admin/settings/approval-flows"],
            Media: []),
        new(
            Slug: "yonetici-adina-izin-ekleme",
            Category: "leave_and_overtime",
            TitleTr: "Yönetici, çalışan adına nasıl izin ekler?",
            TitleEn: "How a manager adds leave on an employee's behalf",
            BodyTr: "Çalışan kartı > İzin sekmesi > sağ üst İşlemler > İzin Ekle'den tür, başlangıç ve bitiş " +
                    "tarihini girin. \"Onay sürecine dahil edilmeden onaylansın\" açıksa talep onaya gitmeden direkt " +
                    "kaydedilir (bu işlem denetim kaydına yazılır); kapalıysa normal onay akışına girer. " +
                    "\"Çalışanı bilgilendirme e-postası gönder\" kapalıyken e-posta gitmez ama izin çalışan kartında " +
                    "yine görünür.",
            BodyEn: "From the employee card > Leave tab > Actions (top right) > Add leave, enter the type, start and " +
                    "end date. With \"Approve without entering the approval flow\" on, the request is recorded " +
                    "directly without approval (this is written to the audit log); with it off, the request enters " +
                    "the normal approval flow. With \"Send notification e-mail to the employee\" off, no e-mail is " +
                    "sent but the leave still shows on the employee's card.",
            RelatedRoutes: ["/app/employees"],
            Media: []),
        new(
            Slug: "erisim-ayarlari",
            Category: "access_and_permissions",
            TitleTr: "Erişim ayarları ne işe yarar?",
            TitleEn: "What do access settings control?",
            BodyTr: "Ayarlar > Sistem ayarları > Erişim ayarları, çalışanlara açılan görünürlük anahtarlarıdır " +
                    "(örgüt şeması, takvim, dosyalarım, zimmetlerim gibi) — bir yetki değil, bir görünürlük " +
                    "anahtarıdır. Owner/Admin/HR bu anahtarlardan etkilenmez, her zaman tam erişime sahiptir.",
            BodyEn: "Settings > System settings > Access settings are visibility toggles exposed to employees (org " +
                    "chart, calendar, my files, my assets, etc.) — a visibility switch, not a permission. Owner/Admin/HR " +
                    "are never restricted by these toggles; they always have full access.",
            RelatedRoutes: ["/admin/settings/system/access"],
            Media: []),
        new(
            Slug: "destek-erisimi-onayi",
            Category: "other",
            TitleTr: "Destek erişimi (impersonation) onayı nasıl çalışır?",
            TitleEn: "How support-access (impersonation) approval works",
            BodyTr: "Sense destek ekibi, hesabınıza yalnızca sizin onayladığınız ve süresi sınırlı bir erişim " +
                    "izniyle bakabilir. Ayarlar > Sistem ayarları > Destek erişimi'nden bekleyen talepleri " +
                    "onaylayabilir veya iptal edebilirsiniz; onaylı bir oturum sürerken üst menüde bir uyarı bandı " +
                    "görünür ve tüm işlemler denetim kaydına yazılır.",
            BodyEn: "Sense support staff can only view your account through an access grant that you approved and " +
                    "that expires. From Settings > System settings > Support access you can approve or revoke " +
                    "pending requests; while a grant is active a warning banner shows at the top and every action " +
                    "is written to the audit log.",
            RelatedRoutes: ["/admin/settings/system/support-access"],
            Media: []),
    ];

    /// <summary>İstek kültürüne göre başlık (RequestLocalizationMiddleware CurrentUICulture'ı Accept-Language'dan kurar; SenseOptions.SupportedCultures: tr/en).</summary>
    public static string LocalizedTitle(this HelpArticle article) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? article.TitleEn : article.TitleTr;

    public static string LocalizedBody(this HelpArticle article) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? article.BodyEn : article.BodyTr;

    public static HelpArticle? GetBySlug(string slug) =>
        Articles.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>rota bağlamlı yardım (sayfa üstü "?" paneli): route için tanımlı makaleleri döner.</summary>
    public static IReadOnlyList<HelpArticle> ForRoute(string route) =>
        Articles.Where(a => a.RelatedRoutes.Any(r => route.StartsWith(r, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>Anahtar kelime araması: başlık + içerik, Türkçe kültürle küçük harfe çevrilir (İ/ı doğru eşleşsin diye).</summary>
    public static IReadOnlyList<HelpArticle> Search(string term)
    {
        var normalized = Normalize(term);
        if (normalized.Length == 0)
        {
            return Articles;
        }

        return Articles
            .Where(a => Normalize(a.TitleTr).Contains(normalized, StringComparison.Ordinal)
                || Normalize(a.TitleEn).Contains(normalized, StringComparison.Ordinal)
                || Normalize(a.BodyTr).Contains(normalized, StringComparison.Ordinal)
                || Normalize(a.BodyEn).Contains(normalized, StringComparison.Ordinal))
            .ToList();
    }

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static string Normalize(string text) => text.ToLower(Turkish).Trim();
}

/// <summary>IHelpArticleReader'ın gömülü içerik uygulaması (Faz 7: Ai modülü RAG kaynağı için).</summary>
public sealed class HelpArticleReader : IHelpArticleReader
{
    public Task<IReadOnlyList<HelpArticleContentDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HelpArticleContentDto> dtos = HelpArticleCatalog.Articles
            .Select(a => new HelpArticleContentDto(a.Slug, a.Category, a.TitleTr, a.TitleEn, a.BodyTr, a.BodyEn, a.RelatedRoutes))
            .ToList();

        return Task.FromResult(dtos);
    }
}
