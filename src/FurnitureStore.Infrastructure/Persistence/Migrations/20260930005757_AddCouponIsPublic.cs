using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponIsPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Coupons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Databases created before this column existed: list the two demo offers to customers, as new seeds do.
            migrationBuilder.Sql("UPDATE Coupons SET IsPublic = 1 WHERE Code IN ('CHAOBAN10', 'GIAM500K')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Coupons");
        }
    }
}
