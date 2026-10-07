using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeBannerAndLogoSubtitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoSubtitle",
                table: "StoreInformation",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            // The logo always showed "FURNITURE" under the name until now: keep it for existing stores.
            migrationBuilder.Sql("UPDATE [StoreInformation] SET [LogoSubtitle] = N'Furniture' WHERE [LogoSubtitle] IS NULL;");

            migrationBuilder.CreateTable(
                name: "HomeBanners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Eyebrow = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TitleHighlight = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PrimaryButtonText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PrimaryButtonUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SecondaryButtonText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SecondaryButtonUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Stat1Value = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Stat1Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Stat2Value = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Stat2Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Stat3Value = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Stat3Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImageWidth = table.Column<int>(type: "int", nullable: true),
                    ImageHeight = table.Column<int>(type: "int", nullable: true),
                    ImageAlt = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeBanners", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeBanners");

            migrationBuilder.DropColumn(
                name: "LogoSubtitle",
                table: "StoreInformation");
        }
    }
}
