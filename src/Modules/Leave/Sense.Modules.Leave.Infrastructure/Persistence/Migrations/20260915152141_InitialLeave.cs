using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sense.Modules.Leave.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "leave");

            migrationBuilder.CreateTable(
                name: "employee_leave_profiles",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    leave_accrual_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_leave_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "leave",
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
                name: "leave_requests",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_code_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    calculated_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_no = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legislation_parameters",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    decimal_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    value_json = table.Column<string>(type: "jsonb", maxLength: 500, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legislation_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "leave",
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
                name: "rule_sets",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_system_seed = table.Column<bool>(type: "boolean", nullable: false),
                    unpaid_leave_shifts_accrual = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    limit_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    per_request_max_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    annual_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    age_exception_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    age_exception_min_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    negative_balance = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    negative_balance_limit_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    accrual_frequency = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    accrual_timing = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    accrual_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    carry_over_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    carry_over_max_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    min_consecutive_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    max_consecutive_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requires_description = table.Column<bool>(type: "boolean", nullable: false),
                    requires_substitute = table.Column<bool>(type: "boolean", nullable: false),
                    requires_document = table.Column<bool>(type: "boolean", nullable: false),
                    waiting_period_months = table.Column<int>(type: "integer", nullable: false),
                    counts_non_working_days = table.Column<bool>(type: "boolean", nullable: false),
                    counts_toward_accrual_service = table.Column<bool>(type: "boolean", nullable: false),
                    show_type_name_on_calendar = table.Column<bool>(type: "boolean", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    allow_self_service_request = table.Column<bool>(type: "boolean", nullable: false),
                    is_disabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_statutory = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seniority_tiers = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_types_rule_sets_rule_set_id",
                        column: x => x.rule_set_id,
                        principalSchema: "leave",
                        principalTable: "rule_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "id", "country", "created_at", "created_user_id", "decimal_value", "effective_from", "effective_to", "key", "modified_date", "modified_user_id", "source", "value_json" },
                values: new object[,]
                {
                    { new Guid("00000008-0000-0000-0000-000000000001"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 14m, new DateOnly(2003, 6, 10), null, "Leave.Annual.Tier1.Days", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 1-5 yıl (5 dahil)", null },
                    { new Guid("00000008-0000-0000-0000-000000000002"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 5m, new DateOnly(2003, 6, 10), null, "Leave.Annual.Tier2.AfterYears", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 5 yıldan fazla", null },
                    { new Guid("00000008-0000-0000-0000-000000000003"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 20m, new DateOnly(2003, 6, 10), null, "Leave.Annual.Tier2.Days", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 5 yıldan fazla, 15 yıldan az", null },
                    { new Guid("00000008-0000-0000-0000-000000000004"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 15m, new DateOnly(2003, 6, 10), null, "Leave.Annual.Tier3.AfterYears", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 15 yıl (dahil) ve fazla", null },
                    { new Guid("00000008-0000-0000-0000-000000000005"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 26m, new DateOnly(2003, 6, 10), null, "Leave.Annual.Tier3.Days", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 15 yıl (dahil) ve fazla", null },
                    { new Guid("00000008-0000-0000-0000-000000000006"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 18m, new DateOnly(2003, 6, 10), null, "Leave.Annual.YoungAgeMax", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 18 ve daha küçük yaştaki işçiler", null },
                    { new Guid("00000008-0000-0000-0000-000000000007"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 50m, new DateOnly(2003, 6, 10), null, "Leave.Annual.OldAgeMin", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - 50 ve daha yukarı yaştaki işçiler", null },
                    { new Guid("00000008-0000-0000-0000-000000000008"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 20m, new DateOnly(2003, 6, 10), null, "Leave.Annual.AgeExceptionMinDays", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - yaş istisnası en az 20 gün", null },
                    { new Guid("00000008-0000-0000-0000-000000000009"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 4m, new DateOnly(2003, 6, 10), null, "Leave.Annual.UndergroundExtraDays", null, null, "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134) - yeraltı işlerinde +4 gün", null },
                    { new Guid("00000008-0000-0000-0000-000000000010"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 5m, new DateOnly(2016, 2, 10), new DateOnly(2026, 4, 30), "Leave.Paternity.Days", null, null, "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620) - eşi doğum yapan işçiye 5 gün", null },
                    { new Guid("00000008-0000-0000-0000-000000000011"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 10m, new DateOnly(2026, 5, 1), null, "Leave.Paternity.Days", null, null, "4857 s. İş Kanunu Ek md.2, 7578 s.K. ile değişik (RG 01.05.2026/33240) - 10 gün (doğum tarihi referans)", null },
                    { new Guid("00000008-0000-0000-0000-000000000012"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 8m, new DateOnly(2003, 6, 10), null, "Leave.Maternity.PreBirthWeeks", null, null, "4857 s. İş Kanunu md.74 (RG 10.06.2003/25134) - doğumdan önce 8 hafta", null },
                    { new Guid("00000008-0000-0000-0000-000000000013"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 8m, new DateOnly(2003, 6, 10), new DateOnly(2026, 4, 30), "Leave.Maternity.PostBirthWeeks", null, null, "4857 s. İş Kanunu md.74 (RG 10.06.2003/25134) - doğumdan sonra 8 hafta", null },
                    { new Guid("00000008-0000-0000-0000-000000000014"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 16m, new DateOnly(2026, 5, 1), null, "Leave.Maternity.PostBirthWeeks", null, null, "4857 s. İş Kanunu md.74, 7578 s.K. ile değişik (RG 01.05.2026/33240) - doğumdan sonra 16 hafta", null },
                    { new Guid("00000008-0000-0000-0000-000000000015"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 3m, new DateOnly(2016, 2, 10), null, "Leave.Marriage.Days", null, null, "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620) - evlenme 3 gün", null },
                    { new Guid("00000008-0000-0000-0000-000000000016"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 3m, new DateOnly(2016, 2, 10), null, "Leave.Adoption.Days", null, null, "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620) - evlat edinme 3 gün", null },
                    { new Guid("00000008-0000-0000-0000-000000000017"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 3m, new DateOnly(2016, 2, 10), null, "Leave.Bereavement.Days", null, null, "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620) - anne/baba/eş/kardeş/çocuk ölümü 3 gün", null },
                    { new Guid("00000008-0000-0000-0000-000000000018"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 10m, new DateOnly(2016, 2, 10), null, "Leave.DisabledChild.DaysPerYear", null, null, "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620) - %70 engelli/süreğen hastalıklı çocuk, yılda 10 güne kadar", null },
                    { new Guid("00000008-0000-0000-0000-000000000019"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 10m, new DateOnly(2026, 5, 1), null, "Leave.FosterCare.Days", null, null, "7578 s.K. (RG 01.05.2026/33240) - koruyucu aile izni 10 gün ücretsiz", null },
                    { new Guid("00000008-0000-0000-0000-000000000020"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 21m, new DateOnly(2019, 6, 26), null, "Leave.PaidMilitaryService.Days", null, null, "1111 s. Askerlik Kanunu geçici md.55 - bedelli askerlik temel eğitimi 21 gün (yürürlük tarihi doğrulanmalı)", null },
                    { new Guid("00000008-0000-0000-0000-000000000021"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 2m, new DateOnly(2003, 6, 10), null, "Leave.JobSearch.MinHoursPerDay", null, null, "4857 s. İş Kanunu md.27 - yeni iş arama izni günde en az 2 saat", null },
                    { new Guid("00000008-0000-0000-0000-000000000022"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 270m, new DateOnly(2003, 6, 10), null, "Overtime.AnnualLimitHours", null, null, "4857 s. İş Kanunu md.41 (RG 10.06.2003/25134) - yıllık 270 saat", null },
                    { new Guid("00000008-0000-0000-0000-000000000023"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 6m, new DateOnly(2003, 6, 10), null, "Overtime.CompTimeExpiryMonths", null, null, "4857 s. İş Kanunu md.41 (RG 10.06.2003/25134) - serbest zaman 6 ay içinde kullanılır", null },
                    { new Guid("00000008-0000-0000-0000-000000000024"), "TR", new DateTime(2026, 9, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 30m, new DateOnly(2017, 8, 25), null, "Overtime.ConsentRevocationNoticeDays", null, null, "Fazla Çalışma Yönetmeliği md.9 (RG 25.08.2017/30165) - onay 30 gün önceden yazılı bildirimle geri alınır", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_leave_profiles_tenant_id",
                schema: "leave",
                table: "employee_leave_profiles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_leave_profiles_tenant_id_employee_id",
                schema: "leave",
                table: "employee_leave_profiles",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_leave_profiles_tenant_id_rule_set_id",
                schema: "leave",
                table: "employee_leave_profiles",
                columns: new[] { "tenant_id", "rule_set_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_id",
                schema: "leave",
                table: "leave_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_id_approval_request_id",
                schema: "leave",
                table: "leave_requests",
                columns: new[] { "tenant_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_id_employee_id_start_date",
                schema: "leave",
                table: "leave_requests",
                columns: new[] { "tenant_id", "employee_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_rule_set_id",
                schema: "leave",
                table: "leave_types",
                column: "rule_set_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id",
                schema: "leave",
                table: "leave_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_rule_set_id",
                schema: "leave",
                table: "leave_types",
                columns: new[] { "tenant_id", "rule_set_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id",
                schema: "leave",
                table: "ledger_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id_employee_id_leave_type_id_effectiv",
                schema: "leave",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tenant_id_idempotency_key",
                schema: "leave",
                table: "ledger_entries",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_legislation_parameters_country_key_effective_from",
                schema: "leave",
                table: "legislation_parameters",
                columns: new[] { "country", "key", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_next_attempt_at",
                schema: "leave",
                table: "outbox_messages",
                columns: new[] { "processed_at", "next_attempt_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "leave",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rule_sets_tenant_id",
                schema: "leave",
                table: "rule_sets",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_leave_profiles",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "leave_requests",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "leave_types",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "legislation_parameters",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "rule_sets",
                schema: "leave");
        }
    }
}
