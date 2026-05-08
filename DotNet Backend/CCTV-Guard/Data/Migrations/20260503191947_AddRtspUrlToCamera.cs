using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTV_Guard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRtspUrlToCamera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RtspUrl",
                table: "Cameras",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RtspUrl",
                table: "Cameras");
        }
    }
}
