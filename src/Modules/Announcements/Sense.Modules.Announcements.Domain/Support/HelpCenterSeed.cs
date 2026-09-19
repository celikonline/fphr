namespace Sense.Modules.Announcements.Domain.Support;

/// <summary>Seed row for a platform help category (deterministic id, applied via EF HasData).</summary>
public sealed record HelpCategorySeedRow(Guid Id, string Slug, string Locale, string Name, int SortOrder);

/// <summary>Seed row for a platform help article (deterministic id, applied via EF HasData; always Published).</summary>
public sealed record HelpArticleSeedRow(
    Guid Id, string Slug, string Locale, Guid CategoryId, string Title, string Summary, string Body,
    IReadOnlyList<string> Tags, IReadOnlyList<string> RelatedRoutes);

/// <summary>
/// Help Center starter content (11.3.13 "Seed": veri aktarımı, izin kuralları, onay süreçleri, erişim ayarları - each
/// with an empty video field), in both tr and en. Platform data, identical for every tenant. Bodies are plain
/// text/markdown-lite ("## " headings, "- " bullets, "1. " steps) and describe the current SenseİK screens.
/// Changing a row here requires a new EF migration (HasData), same as LegislationParameterSeed.
/// </summary>
public static class HelpCenterSeed
{
    public static readonly DateTime PublishedAt = new(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Guid CatDataTransferTr = Id(1);
    private static readonly Guid CatLeaveTr = Id(2);
    private static readonly Guid CatApprovalsTr = Id(3);
    private static readonly Guid CatSettingsTr = Id(4);
    private static readonly Guid CatDataTransferEn = Id(11);
    private static readonly Guid CatLeaveEn = Id(12);
    private static readonly Guid CatApprovalsEn = Id(13);
    private static readonly Guid CatSettingsEn = Id(14);

    private const string DataTransferRoute = "/app/settings/data-transfer";
    private const string LeaveRulesRoute = "/app/settings/leave/rule-sets";
    private const string ApprovalFlowsRoute = "/app/settings/company/approval-flows";
    private const string AccessSettingsRoute = "/app/settings/system/access";

    public static IReadOnlyList<HelpCategorySeedRow> Categories { get; } =
    [
        new(CatDataTransferTr, "veri-aktarimi", HelpLocales.Turkish, "Veri aktarımı", 1),
        new(CatLeaveTr, "izinler", HelpLocales.Turkish, "İzinler", 2),
        new(CatApprovalsTr, "onay-surecleri", HelpLocales.Turkish, "Onay süreçleri", 3),
        new(CatSettingsTr, "ayarlar", HelpLocales.Turkish, "Ayarlar ve erişim", 4),
        new(CatDataTransferEn, "data-transfer", HelpLocales.English, "Data transfer", 1),
        new(CatLeaveEn, "leave", HelpLocales.English, "Leave", 2),
        new(CatApprovalsEn, "approval-flows", HelpLocales.English, "Approval flows", 3),
        new(CatSettingsEn, "settings", HelpLocales.English, "Settings and access", 4),
    ];

    public static IReadOnlyList<HelpArticleSeedRow> Articles { get; } =
    [
        new(
            Id(101), "veri-aktarimi-izin-bilgileri", HelpLocales.Turkish, CatDataTransferTr,
            "Veri aktarımı: İzin bilgileri",
            "Geçmiş izin günlerini ve çalışan bilgilerini Excel şablonuyla toplu olarak SenseİK'e aktarın.",
            """
            ## Ne işe yarar?
            Başka bir sistemden geçerken çalışanlarınızın özlük bilgilerini ve geçmiş izin günlerini tek tek girmek yerine Excel şablonu ile toplu aktarabilirsiniz.

            ## Adımlar
            1. Ayarlar > Veri aktar sayfasını açın.
            2. Aktarmak istediğiniz veri türünü seçin (ör. İzin bilgileri) ve şablonu indirin.
            3. Şablondaki zorunlu kolonları, şablonda belirtilen biçimde doldurun.
            4. Dosyayı yükleyin ve önizleme ekranındaki hataları kontrol edin.
            5. Hatasız satırları onaylayarak aktarımı tamamlayın.

            ## İpuçları
            - Çalışanları, izin bilgilerinden önce aktarın; izin satırları mevcut çalışan kayıtlarıyla eşleştirilir.
            - Aktarımdan önce küçük bir dosyayla deneme yapmanız hataları erken görmenizi sağlar.
            """,
            ["veri aktarımı", "excel", "izin günleri", "içe aktarma", "şablon"], [DataTransferRoute]),
        new(
            Id(102), "izin-kurallari", HelpLocales.Turkish, CatLeaveTr,
            "İzin kuralları nasıl tanımlanır?",
            "İzin türlerini, hak ediş ve devir kurallarını kural setleriyle yönetin ve çalışanlara atayın.",
            """
            ## Kural seti nedir?
            Kural seti; yıllık izin hak edişi, devir, eksi bakiye ve talep kurallarını bir arada tutan şablondur. Her çalışan bir kural setine bağlıdır.

            ## Adımlar
            1. İzinler > Kurallar sayfasını açın.
            2. Varsayılan kural setini kopyalayarak yeni bir set oluşturun veya mevcut seti düzenleyin.
            3. İzin türü ekleyin: limit tipi (limitsiz, talep başına, yıl içinde limitli), devir ve kıdem ayarlarını seçin.
            4. Kaydedin ve kural setini ilgili çalışanlara atayın.

            ## Bilmeniz gerekenler
            - Yasal yıllık izin süreleri (14/20/26 gün) mevzuat parametrelerinden gelir.
            - Kural seti değiştiğinde bakiyeler işe giriş tarihinden itibaren yeniden hesaplanır.
            """,
            ["izin", "kural seti", "yıllık izin", "hak ediş", "devir"], [LeaveRulesRoute]),
        new(
            Id(103), "onay-surecleri", HelpLocales.Turkish, CatApprovalsTr,
            "Onay süreçleri",
            "İzin, avans ve harcama taleplerinin hangi sırayla kimlerin onayına gideceğini belirleyin.",
            """
            ## Onay süreci nedir?
            Onay süreci, bir talebin (izin, avans, harcama, fazla mesai) sonuçlanmadan önce geçeceği onay adımlarıdır. Örneğin önce yönetici, ardından İK onayı.

            ## Adımlar
            1. Ayarlar > Şirket bilgileri > Onay süreçleri sayfasını açın.
            2. Yeni süreç ekleyin ve adımları sırayla tanımlayın (yönetici, belirli bir kişi, rol).
            3. Süreci talep türlerine ve gerekirse birimlere atayın.

            ## İpuçları
            - Onaycılar bekleyen talepleri ana sayfada ve e-posta ile görür.
            - Talep sahibi, talebin hangi adımda beklediğini talep detayında izleyebilir.
            """,
            ["onay", "onay süreci", "talep", "yönetici onayı", "iş akışı"], [ApprovalFlowsRoute]),
        new(
            Id(104), "erisim-ayarlari", HelpLocales.Turkish, CatSettingsTr,
            "Erişim ayarları",
            "Çalışanların ana sayfada, hesaplarında ve takvimde neleri görebileceğini tek sayfadan yönetin.",
            """
            ## Neleri yönetebilirsiniz?
            - Ana sayfa: yaklaşan izinler, yaklaşan doğum günleri, etkinlik takvimi.
            - Hesap: dosyalar, zimmetler, telefon numaraları, organizasyon şeması, özel alanlar.
            - Takvim: takvim menüsü ve takvimdeki tüm izinler.

            ## Adımlar
            1. Ayarlar > Sistem ayarları > Erişim ayarları sayfasını açın.
            2. İlgili anahtarları açın veya kapatın.
            3. Kaydet'e tıklayın; değişiklikler menüye ve ana sayfaya anında yansır.

            ## Not
            Özel alanlar ve takvimdeki tüm izinler varsayılan olarak kapalıdır.
            """,
            ["erişim", "yetki", "ana sayfa", "takvim", "etkinlik takvimi"], [AccessSettingsRoute]),

        new(
            Id(111), "data-transfer-leave-days", HelpLocales.English, CatDataTransferEn,
            "Data transfer: Leave days",
            "Bulk-import past leave days and employee records into SenseİK with an Excel template.",
            """
            ## What is it for?
            When moving from another system, you can import employee records and past leave days in bulk with an Excel template instead of entering them one by one.

            ## Steps
            1. Open Settings > Data transfer.
            2. Choose the data type (e.g. Leave days) and download the template.
            3. Fill in the required columns in the format shown in the template.
            4. Upload the file and review the errors on the preview screen.
            5. Confirm the valid rows to complete the import.

            ## Tips
            - Import employees before leave days; leave rows are matched to existing employee records.
            - Trying a small file first helps you spot errors early.
            """,
            ["data transfer", "excel", "leave days", "import", "template"], [DataTransferRoute]),
        new(
            Id(112), "leave-rules", HelpLocales.English, CatLeaveEn,
            "How do I set up leave rules?",
            "Manage leave types, accrual and carry-over rules with rule sets and assign them to employees.",
            """
            ## What is a rule set?
            A rule set bundles the annual leave accrual, carry-over, negative balance and request rules. Every employee is linked to one rule set.

            ## Steps
            1. Open Leave > Policies.
            2. Copy the default rule set to create a new one, or edit an existing set.
            3. Add a leave type: choose the limit type (unlimited, per request, limited per year), carry-over and seniority settings.
            4. Save and assign the rule set to the relevant employees.

            ## Good to know
            - Statutory annual leave durations (14/20/26 days) come from the legislation parameters.
            - When a rule set changes, balances are recalculated from the hire date.
            """,
            ["leave", "rule set", "annual leave", "accrual", "carry-over"], [LeaveRulesRoute]),
        new(
            Id(113), "approval-flows", HelpLocales.English, CatApprovalsEn,
            "Approval flows",
            "Decide in which order and by whom leave, advance and expense requests are approved.",
            """
            ## What is an approval flow?
            An approval flow is the list of approval steps a request (leave, advance, expense, overtime) goes through before it is decided - for example the manager first, then HR.

            ## Steps
            1. Open Settings > Company > Approval flows.
            2. Add a new flow and define its steps in order (manager, a specific person, a role).
            3. Assign the flow to request types and, if needed, to org units.

            ## Tips
            - Approvers see pending requests on the home page and by e-mail.
            - The requester can follow which step the request is waiting on in the request details.
            """,
            ["approval", "approval flow", "request", "manager approval", "workflow"], [ApprovalFlowsRoute]),
        new(
            Id(114), "access-settings", HelpLocales.English, CatSettingsEn,
            "Access settings",
            "Control what employees can see on the home page, in their account and in the calendar from one page.",
            """
            ## What can you control?
            - Home page: upcoming leaves, upcoming birthdays, event calendar.
            - Account: files, assets, phone numbers, org chart, custom fields.
            - Calendar: the calendar menu and all leaves in the calendar.

            ## Steps
            1. Open Settings > System settings > Access settings.
            2. Turn the relevant switches on or off.
            3. Click Save; changes apply to the menu and home page immediately.

            ## Note
            Custom fields and all leaves in the calendar are off by default.
            """,
            ["access", "permissions", "home page", "calendar", "event calendar"], [AccessSettingsRoute]),
    ];

    private static Guid Id(int n) => new($"0000a11c-0000-0000-0000-{n:D12}");
}
