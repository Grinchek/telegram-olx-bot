using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramOlxBot.Migrations
{
    /// <inheritdoc />
    public partial class SetCascadeOnPostFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PendingPayments_posts_PostId",
                table: "PendingPayments");

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments",
                column: "PostId",
                principalTable: "posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PendingPayments_posts_PostId",
                table: "PendingPayments",
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

            migrationBuilder.DropForeignKey(
                name: "FK_PendingPayments_posts_PostId",
                table: "PendingPayments");

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_payments_posts_PostId",
                table: "confirmed_payments",
                column: "PostId",
                principalTable: "posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PendingPayments_posts_PostId",
                table: "PendingPayments",
                column: "PostId",
                principalTable: "posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
