using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletePayrollLeaveAndTaxRelief : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Populate only untouched, empty system drafts. No approval is granted and tenant edits are preserved.
            migrationBuilder.Sql("""
                UPDATE payroll.payroll_incentive_definitions
                SET parameters = CASE template_key
                    WHEN 'TR_5746_GV' THEN '{"rate":{"value":0.8},"taxReliefGrossCapMultiplier":40,"coversEmployerSgk":false,"coversEmployerUnemployment":false,"coversEmployeeSgk":false,"coversEmployeeUnemployment":false}'::jsonb
                    WHEN 'TR_4691_GV' THEN '{"rate":{"value":1},"taxReliefGrossCapMultiplier":40,"coversEmployerSgk":false,"coversEmployerUnemployment":false,"coversEmployeeSgk":false,"coversEmployeeUnemployment":false}'::jsonb
                    WHEN 'TR_4857_MD30' THEN '{"rate":{"value":1},"coverageBase":"minimumWage","capMultiplier":1,"employerSgkRate":{"legislationKey":"Payroll.Sgk.EmployerRate"},"coversEmployerSgk":true,"coversEmployerUnemployment":false,"coversEmployeeSgk":false,"coversEmployeeUnemployment":false}'::jsonb
                    ELSE '{"rate":{"value":0.5},"coverageBase":"actualSgkBase","employerSgkRate":{"legislationKey":"Payroll.Sgk.EmployerRate"},"coversEmployerSgk":true,"coversEmployerUnemployment":false,"coversEmployeeSgk":false,"coversEmployeeUnemployment":false}'::jsonb END,
                    declaration_code = CASE WHEN template_key IN ('TR_5746_SGK','TR_4691_SGK') THEN '05746'
                        WHEN template_key = 'TR_4857_MD30' THEN '14857' ELSE declaration_code END,
                    valid_from = CASE WHEN template_key IN ('TR_5746_GV','TR_4691_GV') THEN GREATEST(valid_from, DATE '2025-08-01') ELSE valid_from END,
                    valid_to = CASE WHEN template_key = 'TR_4857_MD30' THEN valid_to ELSE LEAST(COALESCE(valid_to, DATE '2028-12-31'), DATE '2028-12-31') END
                WHERE template_key IN ('TR_5746_GV','TR_5746_SGK','TR_4691_GV','TR_4691_SGK','TR_4857_MD30')
                    AND NOT is_smmm_approved AND declaration_code IS NULL
                    AND jsonb_strip_nulls(parameters) = '{"coversEmployerSgk":false,"coversEmployerUnemployment":false,"coversEmployeeSgk":false,"coversEmployeeUnemployment":false}'::jsonb;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep tenant configuration: a rollback must not erase edits or approvals made after this migration.
        }
    }
}
