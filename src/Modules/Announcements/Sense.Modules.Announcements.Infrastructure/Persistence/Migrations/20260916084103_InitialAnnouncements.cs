using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sense.Modules.Announcements.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "announcements");

            migrationBuilder.CreateTable(
                name: "announcements",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", maxLength: 20000, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    event_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    meeting_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "help_categories",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_help_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "announcements",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_dead = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    page_context = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "help_articles",
                schema: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "text", maxLength: 50000, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    video_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    related_routes = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_help_articles", x => x.id);
                    table.ForeignKey(
                        name: "fk_help_articles_help_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "announcements",
                        principalTable: "help_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "announcements",
                table: "help_categories",
                columns: new[] { "id", "created_at", "created_user_id", "locale", "modified_date", "modified_user_id", "name", "slug", "sort_order" },
                values: new object[,]
                {
                    { new Guid("0000a11c-0000-0000-0000-000000000001"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, "Veri aktarımı", "veri-aktarimi", 1 },
                    { new Guid("0000a11c-0000-0000-0000-000000000002"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, "İzinler", "izinler", 2 },
                    { new Guid("0000a11c-0000-0000-0000-000000000003"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, "Onay süreçleri", "onay-surecleri", 3 },
                    { new Guid("0000a11c-0000-0000-0000-000000000004"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, "Ayarlar ve erişim", "ayarlar", 4 },
                    { new Guid("0000a11c-0000-0000-0000-000000000011"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, "Data transfer", "data-transfer", 1 },
                    { new Guid("0000a11c-0000-0000-0000-000000000012"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, "Leave", "leave", 2 },
                    { new Guid("0000a11c-0000-0000-0000-000000000013"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, "Approval flows", "approval-flows", 3 },
                    { new Guid("0000a11c-0000-0000-0000-000000000014"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, "Settings and access", "settings", 4 }
                });

            migrationBuilder.InsertData(
                schema: "announcements",
                table: "help_articles",
                columns: new[] { "id", "body", "category_id", "created_at", "created_user_id", "locale", "modified_date", "modified_user_id", "published_at", "related_routes", "slug", "status", "summary", "tags", "title", "video_url" },
                values: new object[,]
                {
                    { new Guid("0000a11c-0000-0000-0000-000000000101"), "## Ne işe yarar?\nBaşka bir sistemden geçerken çalışanlarınızın özlük bilgilerini ve geçmiş izin günlerini tek tek girmek yerine Excel şablonu ile toplu aktarabilirsiniz.\n\n## Adımlar\n1. Ayarlar > Veri aktar sayfasını açın.\n2. Aktarmak istediğiniz veri türünü seçin (ör. İzin bilgileri) ve şablonu indirin.\n3. Şablondaki zorunlu kolonları, şablonda belirtilen biçimde doldurun.\n4. Dosyayı yükleyin ve önizleme ekranındaki hataları kontrol edin.\n5. Hatasız satırları onaylayarak aktarımı tamamlayın.\n\n## İpuçları\n- Çalışanları, izin bilgilerinden önce aktarın; izin satırları mevcut çalışan kayıtlarıyla eşleştirilir.\n- Aktarımdan önce küçük bir dosyayla deneme yapmanız hataları erken görmenizi sağlar.", new Guid("0000a11c-0000-0000-0000-000000000001"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/data-transfer" }, "veri-aktarimi-izin-bilgileri", "Published", "Geçmiş izin günlerini ve çalışan bilgilerini Excel şablonuyla toplu olarak SenseİK'e aktarın.", new List<string> { "veri aktarımı", "excel", "izin günleri", "içe aktarma", "şablon" }, "Veri aktarımı: İzin bilgileri", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000102"), "## Kural seti nedir?\nKural seti; yıllık izin hak edişi, devir, eksi bakiye ve talep kurallarını bir arada tutan şablondur. Her çalışan bir kural setine bağlıdır.\n\n## Adımlar\n1. İzinler > Kurallar sayfasını açın.\n2. Varsayılan kural setini kopyalayarak yeni bir set oluşturun veya mevcut seti düzenleyin.\n3. İzin türü ekleyin: limit tipi (limitsiz, talep başına, yıl içinde limitli), devir ve kıdem ayarlarını seçin.\n4. Kaydedin ve kural setini ilgili çalışanlara atayın.\n\n## Bilmeniz gerekenler\n- Yasal yıllık izin süreleri (14/20/26 gün) mevzuat parametrelerinden gelir.\n- Kural seti değiştiğinde bakiyeler işe giriş tarihinden itibaren yeniden hesaplanır.", new Guid("0000a11c-0000-0000-0000-000000000002"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/leave/rule-sets" }, "izin-kurallari", "Published", "İzin türlerini, hak ediş ve devir kurallarını kural setleriyle yönetin ve çalışanlara atayın.", new List<string> { "izin", "kural seti", "yıllık izin", "hak ediş", "devir" }, "İzin kuralları nasıl tanımlanır?", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000103"), "## Onay süreci nedir?\nOnay süreci, bir talebin (izin, avans, harcama, fazla mesai) sonuçlanmadan önce geçeceği onay adımlarıdır. Örneğin önce yönetici, ardından İK onayı.\n\n## Adımlar\n1. Ayarlar > Şirket bilgileri > Onay süreçleri sayfasını açın.\n2. Yeni süreç ekleyin ve adımları sırayla tanımlayın (yönetici, belirli bir kişi, rol).\n3. Süreci talep türlerine ve gerekirse birimlere atayın.\n\n## İpuçları\n- Onaycılar bekleyen talepleri ana sayfada ve e-posta ile görür.\n- Talep sahibi, talebin hangi adımda beklediğini talep detayında izleyebilir.", new Guid("0000a11c-0000-0000-0000-000000000003"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/company/approval-flows" }, "onay-surecleri", "Published", "İzin, avans ve harcama taleplerinin hangi sırayla kimlerin onayına gideceğini belirleyin.", new List<string> { "onay", "onay süreci", "talep", "yönetici onayı", "iş akışı" }, "Onay süreçleri", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000104"), "## Neleri yönetebilirsiniz?\n- Ana sayfa: yaklaşan izinler, yaklaşan doğum günleri, etkinlik takvimi.\n- Hesap: dosyalar, zimmetler, telefon numaraları, organizasyon şeması, özel alanlar.\n- Takvim: takvim menüsü ve takvimdeki tüm izinler.\n\n## Adımlar\n1. Ayarlar > Sistem ayarları > Erişim ayarları sayfasını açın.\n2. İlgili anahtarları açın veya kapatın.\n3. Kaydet'e tıklayın; değişiklikler menüye ve ana sayfaya anında yansır.\n\n## Not\nÖzel alanlar ve takvimdeki tüm izinler varsayılan olarak kapalıdır.", new Guid("0000a11c-0000-0000-0000-000000000004"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "tr", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/system/access" }, "erisim-ayarlari", "Published", "Çalışanların ana sayfada, hesaplarında ve takvimde neleri görebileceğini tek sayfadan yönetin.", new List<string> { "erişim", "yetki", "ana sayfa", "takvim", "etkinlik takvimi" }, "Erişim ayarları", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000111"), "## What is it for?\nWhen moving from another system, you can import employee records and past leave days in bulk with an Excel template instead of entering them one by one.\n\n## Steps\n1. Open Settings > Data transfer.\n2. Choose the data type (e.g. Leave days) and download the template.\n3. Fill in the required columns in the format shown in the template.\n4. Upload the file and review the errors on the preview screen.\n5. Confirm the valid rows to complete the import.\n\n## Tips\n- Import employees before leave days; leave rows are matched to existing employee records.\n- Trying a small file first helps you spot errors early.", new Guid("0000a11c-0000-0000-0000-000000000011"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/data-transfer" }, "data-transfer-leave-days", "Published", "Bulk-import past leave days and employee records into SenseİK with an Excel template.", new List<string> { "data transfer", "excel", "leave days", "import", "template" }, "Data transfer: Leave days", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000112"), "## What is a rule set?\nA rule set bundles the annual leave accrual, carry-over, negative balance and request rules. Every employee is linked to one rule set.\n\n## Steps\n1. Open Leave > Policies.\n2. Copy the default rule set to create a new one, or edit an existing set.\n3. Add a leave type: choose the limit type (unlimited, per request, limited per year), carry-over and seniority settings.\n4. Save and assign the rule set to the relevant employees.\n\n## Good to know\n- Statutory annual leave durations (14/20/26 days) come from the legislation parameters.\n- When a rule set changes, balances are recalculated from the hire date.", new Guid("0000a11c-0000-0000-0000-000000000012"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/leave/rule-sets" }, "leave-rules", "Published", "Manage leave types, accrual and carry-over rules with rule sets and assign them to employees.", new List<string> { "leave", "rule set", "annual leave", "accrual", "carry-over" }, "How do I set up leave rules?", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000113"), "## What is an approval flow?\nAn approval flow is the list of approval steps a request (leave, advance, expense, overtime) goes through before it is decided - for example the manager first, then HR.\n\n## Steps\n1. Open Settings > Company > Approval flows.\n2. Add a new flow and define its steps in order (manager, a specific person, a role).\n3. Assign the flow to request types and, if needed, to org units.\n\n## Tips\n- Approvers see pending requests on the home page and by e-mail.\n- The requester can follow which step the request is waiting on in the request details.", new Guid("0000a11c-0000-0000-0000-000000000013"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/company/approval-flows" }, "approval-flows", "Published", "Decide in which order and by whom leave, advance and expense requests are approved.", new List<string> { "approval", "approval flow", "request", "manager approval", "workflow" }, "Approval flows", null },
                    { new Guid("0000a11c-0000-0000-0000-000000000114"), "## What can you control?\n- Home page: upcoming leaves, upcoming birthdays, event calendar.\n- Account: files, assets, phone numbers, org chart, custom fields.\n- Calendar: the calendar menu and all leaves in the calendar.\n\n## Steps\n1. Open Settings > System settings > Access settings.\n2. Turn the relevant switches on or off.\n3. Click Save; changes apply to the menu and home page immediately.\n\n## Note\nCustom fields and all leaves in the calendar are off by default.", new Guid("0000a11c-0000-0000-0000-000000000014"), new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "en", null, null, new DateTime(2026, 9, 16, 0, 0, 0, 0, DateTimeKind.Utc), new List<string> { "/app/settings/system/access" }, "access-settings", "Published", "Control what employees can see on the home page, in their account and in the calendar from one page.", new List<string> { "access", "permissions", "home page", "calendar", "event calendar" }, "Access settings", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_tenant_id",
                schema: "announcements",
                table: "announcements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_tenant_id_category_event_starts_at",
                schema: "announcements",
                table: "announcements",
                columns: new[] { "tenant_id", "category", "event_starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_tenant_id_status_is_pinned_published_at",
                schema: "announcements",
                table: "announcements",
                columns: new[] { "tenant_id", "status", "is_pinned", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_help_articles_category_id",
                schema: "announcements",
                table: "help_articles",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_help_articles_locale_status_category_id",
                schema: "announcements",
                table: "help_articles",
                columns: new[] { "locale", "status", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_help_articles_slug",
                schema: "announcements",
                table: "help_articles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_help_categories_locale_slug",
                schema: "announcements",
                table: "help_categories",
                columns: new[] { "locale", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "announcements",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "announcements",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_tenant_id",
                schema: "announcements",
                table: "support_tickets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_tenant_id_user_id_created_at",
                schema: "announcements",
                table: "support_tickets",
                columns: new[] { "tenant_id", "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements",
                schema: "announcements");

            migrationBuilder.DropTable(
                name: "help_articles",
                schema: "announcements");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "announcements");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "announcements");

            migrationBuilder.DropTable(
                name: "support_tickets",
                schema: "announcements");

            migrationBuilder.DropTable(
                name: "help_categories",
                schema: "announcements");
        }
    }
}
