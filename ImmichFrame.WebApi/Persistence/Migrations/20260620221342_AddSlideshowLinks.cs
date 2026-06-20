using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmichFrame.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlideshowLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlideshowLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowMemories = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowFavorites = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowVideos = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImagesFromDays = table.Column<int>(type: "INTEGER", nullable: true),
                    ImagesFromDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImagesUntilDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Albums = table.Column<string>(type: "TEXT", nullable: false),
                    ExcludedAlbums = table.Column<string>(type: "TEXT", nullable: false),
                    People = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlideshowLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlideshowLinks_Slug",
                table: "SlideshowLinks",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlideshowLinks");
        }
    }
}
