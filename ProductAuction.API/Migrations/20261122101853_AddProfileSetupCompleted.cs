using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductAuction.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileSetupCompleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The SignUpCompleted column already exists in the database
            // (from a pre-applied migration not present in this repo).
            // This empty migration only syncs the model snapshot with the
            // IsProfileSetupCompleted entity property mapped to that column.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
