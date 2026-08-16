using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitOrderNotificationResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Result",
                table: "OrderNotifications");

            migrationBuilder.AddColumn<string>(
                name: "EmailResult",
                table: "OrderNotifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaciResult",
                table: "OrderNotifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SMSResult",
                table: "OrderNotifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailResult",
                table: "OrderNotifications");

            migrationBuilder.DropColumn(
                name: "PaciResult",
                table: "OrderNotifications");

            migrationBuilder.DropColumn(
                name: "SMSResult",
                table: "OrderNotifications");

            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "OrderNotifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }
    }
}
