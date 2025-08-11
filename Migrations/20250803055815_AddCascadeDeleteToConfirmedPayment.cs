using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramOlxBot.Migrations
{
    /// <inheritdoc />
    public partial class AddCascadeDeleteToConfirmedPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "confirmed_payments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments",
                column: "PostId",
                principalTable: "posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "confirmed_payments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments",
                column: "PostId",
                principalTable: "posts",
                principalColumn: "Id");
        }
    }
}
