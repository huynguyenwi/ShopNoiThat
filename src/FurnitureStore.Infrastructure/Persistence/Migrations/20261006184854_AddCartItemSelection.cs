using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FurnitureStore.Infrastructure.Persistence.Migrations
{
    /// <summary>Cart lines can be ticked: only ticked lines go into the next order, the others stay in the cart.</summary>
    public partial class AddCartItemSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lines already in carts stay ticked, so existing carts order exactly as before.
            migrationBuilder.AddColumn<bool>(
                name: "IsSelected",
                table: "CartItems",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSelected",
                table: "CartItems");
        }
    }
}
