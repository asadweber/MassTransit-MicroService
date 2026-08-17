using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConvertOrderNotificationToOwned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SagaOrderNotification_OrderSagaStates_OrderSagaStateCorrelationId",
                table: "SagaOrderNotification");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SagaOrderNotification",
                table: "SagaOrderNotification");

            migrationBuilder.RenameTable(
                name: "SagaOrderNotification",
                newName: "SagaOrderNotifications");

            migrationBuilder.RenameIndex(
                name: "IX_SagaOrderNotification_OrderSagaStateCorrelationId",
                table: "SagaOrderNotifications",
                newName: "IX_SagaOrderNotifications_OrderSagaStateCorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SagaOrderNotifications",
                table: "SagaOrderNotifications",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SagaOrderNotifications_OrderSagaStates_OrderSagaStateCorrelationId",
                table: "SagaOrderNotifications",
                column: "OrderSagaStateCorrelationId",
                principalTable: "OrderSagaStates",
                principalColumn: "CorrelationId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SagaOrderNotifications_OrderSagaStates_OrderSagaStateCorrelationId",
                table: "SagaOrderNotifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SagaOrderNotifications",
                table: "SagaOrderNotifications");

            migrationBuilder.RenameTable(
                name: "SagaOrderNotifications",
                newName: "SagaOrderNotification");

            migrationBuilder.RenameIndex(
                name: "IX_SagaOrderNotifications_OrderSagaStateCorrelationId",
                table: "SagaOrderNotification",
                newName: "IX_SagaOrderNotification_OrderSagaStateCorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SagaOrderNotification",
                table: "SagaOrderNotification",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SagaOrderNotification_OrderSagaStates_OrderSagaStateCorrelationId",
                table: "SagaOrderNotification",
                column: "OrderSagaStateCorrelationId",
                principalTable: "OrderSagaStates",
                principalColumn: "CorrelationId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
