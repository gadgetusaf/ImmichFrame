using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmichFrame.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "SlideshowLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "SlideshowLinks");
        }
    }
}
