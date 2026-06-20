using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmichFrame.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImmichServerUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKeyFile = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadImages = table.Column<bool>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    ImageLocationFormat = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoDateFormat = table.Column<string>(type: "TEXT", nullable: true),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    TransitionDuration = table.Column<double>(type: "REAL", nullable: false),
                    ShowClock = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClockFormat = table.Column<string>(type: "TEXT", nullable: true),
                    ClockDateFormat = table.Column<string>(type: "TEXT", nullable: true),
                    ShowProgressBar = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowPhotoDate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowImageDesc = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowPeopleDesc = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowTagsDesc = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowAlbumName = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowImageLocation = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrimaryColor = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColor = table.Column<string>(type: "TEXT", nullable: true),
                    Style = table.Column<string>(type: "TEXT", nullable: false),
                    BaseFontSize = table.Column<string>(type: "TEXT", nullable: true),
                    ShowWeatherDescription = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeatherIconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ImageZoom = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImagePan = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImageFill = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayAudio = table.Column<bool>(type: "INTEGER", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    RenewImagesDuration = table.Column<int>(type: "INTEGER", nullable: false),
                    Webcalendars = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshAlbumPeopleInterval = table.Column<int>(type: "INTEGER", nullable: false),
                    WeatherApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    UnitSystem = table.Column<string>(type: "TEXT", nullable: true),
                    WeatherLatLong = table.Column<string>(type: "TEXT", nullable: true),
                    Webhook = table.Column<string>(type: "TEXT", nullable: true),
                    AuthenticationSecret = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "GeneralSettings");
        }
    }
}
