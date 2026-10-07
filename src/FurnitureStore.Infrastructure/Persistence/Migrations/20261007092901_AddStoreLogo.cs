using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LogoHeight",
                table: "StoreInformation",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LogoShowsName",
                table: "StoreInformation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "StoreInformation",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogoWidth",
                table: "StoreInformation",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoHeight",
                table: "StoreInformation");

            migrationBuilder.DropColumn(
                name: "LogoShowsName",
                table: "StoreInformation");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "StoreInformation");

            migrationBuilder.DropColumn(
                name: "LogoWidth",
                table: "StoreInformation");
        }
    }
}
