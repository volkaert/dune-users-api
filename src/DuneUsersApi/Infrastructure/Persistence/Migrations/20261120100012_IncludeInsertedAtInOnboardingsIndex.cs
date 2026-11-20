using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneUsersApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncludeInsertedAtInOnboardingsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_onboarded_at_tenant_user_id",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_onboarded_at_tenant_user_id",
                table: "users",
                columns: new[] { "tenant_id", "onboarded_at", "tenant_user_id" })
                .Annotation("Npgsql:IndexInclude", new[] { "inserted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_onboarded_at_tenant_user_id",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_onboarded_at_tenant_user_id",
                table: "users",
                columns: new[] { "tenant_id", "onboarded_at", "tenant_user_id" });
        }
    }
}
