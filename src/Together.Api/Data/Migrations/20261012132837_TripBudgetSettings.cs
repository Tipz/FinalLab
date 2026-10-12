using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Together.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripBudgetSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BudgetLimitKopecks",
                table: "trips",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReservePercent",
                table: "trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_trips_budget_limit",
                table: "trips",
                sql: "\"BudgetLimitKopecks\" IS NULL OR \"BudgetLimitKopecks\" BETWEEN 0 AND 10000000000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trips_reserve_percent",
                table: "trips",
                sql: "\"ReservePercent\" BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_trips_budget_limit",
                table: "trips");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trips_reserve_percent",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "BudgetLimitKopecks",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ReservePercent",
                table: "trips");
        }
    }
}
