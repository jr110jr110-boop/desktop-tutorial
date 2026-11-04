using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class InvGenealogyLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvGenealogyLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    OutputItemId = table.Column<int>(type: "int", nullable: false),
                    OutputLotId = table.Column<int>(type: "int", nullable: true),
                    OutputSerialUnitId = table.Column<int>(type: "int", nullable: true),
                    InputItemId = table.Column<int>(type: "int", nullable: false),
                    InputLotId = table.Column<int>(type: "int", nullable: true),
                    InputSerialUnitId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsShared = table.Column<bool>(type: "bit", nullable: false),
                    IssueDocumentId = table.Column<int>(type: "int", nullable: true),
                    ReceiptDocumentId = table.Column<int>(type: "int", nullable: false),
                    LinkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvGenealogyLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvDocuments_IssueDocumentId",
                        column: x => x.IssueDocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvDocuments_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvItems_InputItemId",
                        column: x => x.InputItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvItems_OutputItemId",
                        column: x => x.OutputItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvLots_InputLotId",
                        column: x => x.InputLotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvLots_OutputLotId",
                        column: x => x.OutputLotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvSerialUnits_InputSerialUnitId",
                        column: x => x.InputSerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_InvSerialUnits_OutputSerialUnitId",
                        column: x => x.OutputSerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvGenealogyLinks_MfProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "MfProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_InputItemId",
                table: "InvGenealogyLinks",
                column: "InputItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_InputLotId",
                table: "InvGenealogyLinks",
                column: "InputLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_InputSerialUnitId",
                table: "InvGenealogyLinks",
                column: "InputSerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_IssueDocumentId",
                table: "InvGenealogyLinks",
                column: "IssueDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_OperationId",
                table: "InvGenealogyLinks",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_OutputItemId",
                table: "InvGenealogyLinks",
                column: "OutputItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_OutputLotId",
                table: "InvGenealogyLinks",
                column: "OutputLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_OutputSerialUnitId",
                table: "InvGenealogyLinks",
                column: "OutputSerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvGenealogyLinks_ReceiptDocumentId",
                table: "InvGenealogyLinks",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "UX_InvGenealogyLinks_Key",
                table: "InvGenealogyLinks",
                columns: new[] { "ProductionOrderId", "OutputItemId", "OutputLotId", "OutputSerialUnitId", "InputItemId", "InputLotId", "InputSerialUnitId" },
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvGenealogyLinks");
        }
    }
}
