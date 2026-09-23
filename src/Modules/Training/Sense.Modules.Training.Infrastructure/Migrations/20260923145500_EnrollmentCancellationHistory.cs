using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnrollmentCancellationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Enrollments_TenantId_TrainingProgramId_EmployeeId",
                schema: "training",
                table: "Enrollments");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "training",
                table: "Enrollments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                schema: "training",
                table: "Enrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_TenantId_TrainingProgramId_EmployeeId",
                schema: "training",
                table: "Enrollments",
                columns: new[] { "tenant_id", "training_program_id", "employee_id" },
                unique: true,
                filter: "status <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Enrollments_TenantId_TrainingProgramId_EmployeeId",
                schema: "training",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "training",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "training",
                table: "Enrollments");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_TenantId_TrainingProgramId_EmployeeId",
                schema: "training",
                table: "Enrollments",
                columns: new[] { "tenant_id", "training_program_id", "employee_id" },
                unique: true);
        }
    }
}
