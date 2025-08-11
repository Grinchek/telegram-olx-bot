using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramOlxBot.Migrations
{
    /// <inheritdoc />
    public partial class AddPostIdToPendingPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pending_payments_posts_PostId",
                table: "pending_payments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_pending_payments",
                table: "pending_payments");

            migrationBuilder.RenameTable(
                name: "pending_payments",
                newName: "PendingPayments");

            migrationBuilder.RenameIndex(
                name: "IX_pending_payments_PostId",
                table: "PendingPayments",
                newName: "IX_PendingPayments_PostId");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "PendingPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PendingPayments",
                table: "PendingPayments",
                column: "Id");

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
                name: "FK_PendingPayments_posts_PostId",
                table: "PendingPayments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PendingPayments",
                table: "PendingPayments");

            migrationBuilder.RenameTable(
                name: "PendingPayments",
                newName: "pending_payments");

            migrationBuilder.RenameIndex(
                name: "IX_PendingPayments_PostId",
                table: "pending_payments",
                newName: "IX_pending_payments_PostId");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "pending_payments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_pending_payments",
                table: "pending_payments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_pending_payments_posts_PostId",
                table: "pending_payments",
                column: "PostId",
                principalTable: "posts",
                principalColumn: "Id");
        }
    }
}
