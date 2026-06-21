using ImmichFrame.WebApi.Persistence.Entities;

namespace ImmichFrame.WebApi.Models;

/// <summary>
/// Admin-facing representation of the global general (display) settings. The three secret values —
/// <see cref="GeneralSettingsEntity.AuthenticationSecret"/>, <see cref="GeneralSettingsEntity.WeatherApiKey"/>
/// and <see cref="GeneralSettingsEntity.Webhook"/> — are write-only: only the <c>Has*</c> booleans are
/// returned to the browser, and on save a blank/null incoming value keeps the stored secret (exactly like
/// <see cref="AccountDto"/>'s API key). Every other field is fully readable and writable.
/// </summary>
public class GeneralSettingsDto
{
    public bool DownloadImages { get; set; }
    public string Language { get; set; } = "en";
    public string? ImageLocationFormat { get; set; }
    public string? PhotoDateFormat { get; set; }
    public int Interval { get; set; }
    public double TransitionDuration { get; set; }
    public bool ShowClock { get; set; }
    public string? ClockFormat { get; set; }
    public string? ClockDateFormat { get; set; }
    public bool ShowProgressBar { get; set; }
    public bool ShowPhotoDate { get; set; }
    public bool ShowImageDesc { get; set; }
    public bool ShowPeopleDesc { get; set; }
    public bool ShowTagsDesc { get; set; }
    public bool ShowAlbumName { get; set; }
    public bool ShowImageLocation { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string Style { get; set; } = "none";
    public string? BaseFontSize { get; set; }
    public bool ShowWeatherDescription { get; set; }
    public string? WeatherIconUrl { get; set; }
    public bool ImageZoom { get; set; }
    public bool ImagePan { get; set; }
    public bool ImageFill { get; set; }
    public bool PlayAudio { get; set; }
    public string Layout { get; set; } = "splitview";
    public int RenewImagesDuration { get; set; }
    public List<string> Webcalendars { get; set; } = new();
    public int RefreshAlbumPeopleInterval { get; set; }
    public string? UnitSystem { get; set; }
    public string? WeatherLatLong { get; set; }

    // Secrets are write-only: never echo the value, only whether one is set.
    public bool HasAuthenticationSecret { get; set; }
    public string? AuthenticationSecret { get; set; }

    public bool HasWeatherApiKey { get; set; }
    public string? WeatherApiKey { get; set; }

    public bool HasWebhook { get; set; }
    public string? Webhook { get; set; }

    public static GeneralSettingsDto FromEntity(GeneralSettingsEntity e) => new()
    {
        DownloadImages = e.DownloadImages,
        Language = e.Language,
        ImageLocationFormat = e.ImageLocationFormat,
        PhotoDateFormat = e.PhotoDateFormat,
        Interval = e.Interval,
        TransitionDuration = e.TransitionDuration,
        ShowClock = e.ShowClock,
        ClockFormat = e.ClockFormat,
        ClockDateFormat = e.ClockDateFormat,
        ShowProgressBar = e.ShowProgressBar,
        ShowPhotoDate = e.ShowPhotoDate,
        ShowImageDesc = e.ShowImageDesc,
        ShowPeopleDesc = e.ShowPeopleDesc,
        ShowTagsDesc = e.ShowTagsDesc,
        ShowAlbumName = e.ShowAlbumName,
        ShowImageLocation = e.ShowImageLocation,
        PrimaryColor = e.PrimaryColor,
        SecondaryColor = e.SecondaryColor,
        Style = e.Style,
        BaseFontSize = e.BaseFontSize,
        ShowWeatherDescription = e.ShowWeatherDescription,
        WeatherIconUrl = e.WeatherIconUrl,
        ImageZoom = e.ImageZoom,
        ImagePan = e.ImagePan,
        ImageFill = e.ImageFill,
        PlayAudio = e.PlayAudio,
        Layout = e.Layout,
        RenewImagesDuration = e.RenewImagesDuration,
        Webcalendars = new List<string>(e.Webcalendars),
        RefreshAlbumPeopleInterval = e.RefreshAlbumPeopleInterval,
        UnitSystem = e.UnitSystem,
        WeatherLatLong = e.WeatherLatLong,
        HasAuthenticationSecret = !string.IsNullOrEmpty(e.AuthenticationSecret),
        AuthenticationSecret = null,
        HasWeatherApiKey = !string.IsNullOrEmpty(e.WeatherApiKey),
        WeatherApiKey = null,
        HasWebhook = !string.IsNullOrEmpty(e.Webhook),
        Webhook = null,
    };

    /// <summary>
    /// Copies every editable field onto an existing entity. For the three secrets a blank/null incoming
    /// value keeps whatever is already stored; a non-blank value replaces it.
    /// </summary>
    public void ApplyTo(GeneralSettingsEntity e)
    {
        e.DownloadImages = DownloadImages;
        e.Language = Language;
        e.ImageLocationFormat = ImageLocationFormat;
        e.PhotoDateFormat = PhotoDateFormat;
        e.Interval = Interval;
        e.TransitionDuration = TransitionDuration;
        e.ShowClock = ShowClock;
        e.ClockFormat = ClockFormat;
        e.ClockDateFormat = ClockDateFormat;
        e.ShowProgressBar = ShowProgressBar;
        e.ShowPhotoDate = ShowPhotoDate;
        e.ShowImageDesc = ShowImageDesc;
        e.ShowPeopleDesc = ShowPeopleDesc;
        e.ShowTagsDesc = ShowTagsDesc;
        e.ShowAlbumName = ShowAlbumName;
        e.ShowImageLocation = ShowImageLocation;
        e.PrimaryColor = PrimaryColor;
        e.SecondaryColor = SecondaryColor;
        e.Style = Style;
        e.BaseFontSize = BaseFontSize;
        e.ShowWeatherDescription = ShowWeatherDescription;
        e.WeatherIconUrl = WeatherIconUrl;
        e.ImageZoom = ImageZoom;
        e.ImagePan = ImagePan;
        e.ImageFill = ImageFill;
        e.PlayAudio = PlayAudio;
        e.Layout = Layout;
        e.RenewImagesDuration = RenewImagesDuration;
        e.Webcalendars = new List<string>(Webcalendars);
        e.RefreshAlbumPeopleInterval = RefreshAlbumPeopleInterval;
        e.UnitSystem = UnitSystem;
        e.WeatherLatLong = WeatherLatLong;

        // Blank means "keep the existing secret" (same behaviour as AccountDto's API key).
        if (!string.IsNullOrWhiteSpace(AuthenticationSecret))
            e.AuthenticationSecret = AuthenticationSecret.Trim();
        if (!string.IsNullOrWhiteSpace(WeatherApiKey))
            e.WeatherApiKey = WeatherApiKey.Trim();
        if (!string.IsNullOrWhiteSpace(Webhook))
            e.Webhook = Webhook.Trim();
    }
}
