using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneUsersApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    onboarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_address_country_code", "address_country_code ~ '^[A-Z]{2}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_onboarded_at",
                table: "users",
                column: "onboarded_at");

            migrationBuilder.CreateIndex(
                name: "ux_users_tenant_id_tenant_user_id",
                table: "users",
                columns: new[] { "tenant_id", "tenant_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
