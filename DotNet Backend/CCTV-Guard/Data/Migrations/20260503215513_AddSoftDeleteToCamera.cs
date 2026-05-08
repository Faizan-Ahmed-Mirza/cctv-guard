using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTV_Guard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToCamera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Cameras",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Cameras");
        }
    }
}
