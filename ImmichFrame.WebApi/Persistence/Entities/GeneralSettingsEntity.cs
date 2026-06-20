using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.WebApi.Persistence.Entities;

/// <summary>
/// EF Core entity holding the single global <see cref="IGeneralSettings"/> row.
/// Defaults mirror <c>ImmichFrame.WebApi.Models.GeneralSettings</c> so that an
/// unconfigured instance behaves identically to the legacy file-based defaults.
/// </summary>
public class GeneralSettingsEntity : IGeneralSettings
{
    // Single-row table; always persisted with Id = 1.
    public int Id { get; set; } = 1;

    public bool DownloadImages { get; set; } = false;
    public string Language { get; set; } = "en";
    public string? ImageLocationFormat { get; set; } = "City,State,Country";
    public string? PhotoDateFormat { get; set; } = "MM/dd/yyyy";
    public int Interval { get; set; } = 45;
    public double TransitionDuration { get; set; } = 1;
    public bool ShowClock { get; set; } = true;
    public string? ClockFormat { get; set; } = "hh:mm";
    public string? ClockDateFormat { get; set; } = "eee, MMM d";
    public bool ShowProgressBar { get; set; } = true;
    public bool ShowPhotoDate { get; set; } = true;
    public bool ShowImageDesc { get; set; } = true;
    public bool ShowPeopleDesc { get; set; } = true;
    public bool ShowTagsDesc { get; set; } = true;
    public bool ShowAlbumName { get; set; } = true;
    public bool ShowImageLocation { get; set; } = true;
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string Style { get; set; } = "none";
    public string? BaseFontSize { get; set; }
    public bool ShowWeatherDescription { get; set; } = true;
    public string? WeatherIconUrl { get; set; } = "https://openweathermap.org/img/wn/{IconId}.png";
    public bool ImageZoom { get; set; } = true;
    public bool ImagePan { get; set; } = false;
    public bool ImageFill { get; set; } = false;
    public bool PlayAudio { get; set; } = false;
    public string Layout { get; set; } = "splitview";
    public int RenewImagesDuration { get; set; } = 30;
    public List<string> Webcalendars { get; set; } = new();
    public int RefreshAlbumPeopleInterval { get; set; } = 12;
    public string? WeatherApiKey { get; set; } = string.Empty;
    public string? UnitSystem { get; set; } = "imperial";
    public string? WeatherLatLong { get; set; } = "40.7128,74.0060";
    public string? Webhook { get; set; }
    public string? AuthenticationSecret { get; set; }

    public void Validate() { }

    /// <summary>Creates a persistable entity from any <see cref="IGeneralSettings"/> source.</summary>
    public static GeneralSettingsEntity From(IGeneralSettings s) => new()
    {
        Id = 1,
        DownloadImages = s.DownloadImages,
        Language = s.Language,
        ImageLocationFormat = s.ImageLocationFormat,
        PhotoDateFormat = s.PhotoDateFormat,
        Interval = s.Interval,
        TransitionDuration = s.TransitionDuration,
        ShowClock = s.ShowClock,
        ClockFormat = s.ClockFormat,
        ClockDateFormat = s.ClockDateFormat,
        ShowProgressBar = s.ShowProgressBar,
        ShowPhotoDate = s.ShowPhotoDate,
        ShowImageDesc = s.ShowImageDesc,
        ShowPeopleDesc = s.ShowPeopleDesc,
        ShowTagsDesc = s.ShowTagsDesc,
        ShowAlbumName = s.ShowAlbumName,
        ShowImageLocation = s.ShowImageLocation,
        PrimaryColor = s.PrimaryColor,
        SecondaryColor = s.SecondaryColor,
        Style = s.Style,
        BaseFontSize = s.BaseFontSize,
        ShowWeatherDescription = s.ShowWeatherDescription,
        WeatherIconUrl = s.WeatherIconUrl,
        ImageZoom = s.ImageZoom,
        ImagePan = s.ImagePan,
        ImageFill = s.ImageFill,
        PlayAudio = s.PlayAudio,
        Layout = s.Layout,
        RenewImagesDuration = s.RenewImagesDuration,
        Webcalendars = new List<string>(s.Webcalendars),
        RefreshAlbumPeopleInterval = s.RefreshAlbumPeopleInterval,
        WeatherApiKey = s.WeatherApiKey,
        UnitSystem = s.UnitSystem,
        WeatherLatLong = s.WeatherLatLong,
        Webhook = s.Webhook,
        AuthenticationSecret = s.AuthenticationSecret,
    };
}
