using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MailDemon.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lists",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    FromEmailAddress = table.Column<string>(nullable: true),
                    FromEmailName = table.Column<string>(nullable: true),
                    Company = table.Column<string>(nullable: true),
                    PhysicalAddress = table.Column<string>(nullable: true),
                    Website = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ListName = table.Column<string>(nullable: true),
                    LanguageCode = table.Column<string>(nullable: true),
                    IPAddress = table.Column<string>(nullable: true),
                    Expires = table.Column<DateTime>(nullable: false),
                    SubscribedDate = table.Column<DateTime>(nullable: false),
                    UnsubscribedDate = table.Column<DateTime>(nullable: false),
                    SubscribeToken = table.Column<string>(nullable: true),
                    UnsubscribeToken = table.Column<string>(nullable: true),
                    Result = table.Column<string>(nullable: true),
                    ResultTimestamp = table.Column<DateTime>(nullable: false),
                    EmailAddress = table.Column<string>(nullable: true),
                    EmailAddressDomain = table.Column<string>(nullable: true),
                    FieldsJson = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    LastModified = table.Column<DateTime>(nullable: false),
                    Text = table.Column<string>(nullable: true),
                    Dirty = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lists_Name",
                table: "Lists",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_EmailAddress",
                table: "Subscriptions",
                column: "EmailAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_EmailAddressDomain",
                table: "Subscriptions",
                column: "EmailAddressDomain");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ListName",
                table: "Subscriptions",
                column: "ListName");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Result",
                table: "Subscriptions",
                column: "Result");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_SubscribeToken",
                table: "Subscriptions",
                column: "SubscribeToken");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UnsubscribeToken",
                table: "Subscriptions",
                column: "UnsubscribeToken");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Name",
                table: "Templates",
                column: "Name");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lists");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Templates");
        }
    }
}
