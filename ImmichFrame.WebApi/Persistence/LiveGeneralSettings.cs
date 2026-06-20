using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// A stable <see cref="IGeneralSettings"/> façade that always reads from the provider's current
/// snapshot. Registering this as the singleton <see cref="IGeneralSettings"/> means consumers that
/// capture it once (e.g. the weather/calendar singletons) still observe configuration changes the
/// moment <see cref="DatabaseServerSettings.Load"/> swaps in a new snapshot — no restart required.
/// </summary>
public class LiveGeneralSettings(DatabaseServerSettings provider) : IGeneralSettings
{
    private IGeneralSettings S => provider.GeneralSettings;

    public List<string> Webcalendars => S.Webcalendars;
    public int RefreshAlbumPeopleInterval => S.RefreshAlbumPeopleInterval;
    public string? WeatherApiKey => S.WeatherApiKey;
    public string? WeatherLatLong => S.WeatherLatLong;
    public string? UnitSystem => S.UnitSystem;
    public string? Webhook => S.Webhook;
    public string? AuthenticationSecret => S.AuthenticationSecret;
    public int Interval => S.Interval;
    public double TransitionDuration => S.TransitionDuration;
    public bool DownloadImages => S.DownloadImages;
    public int RenewImagesDuration => S.RenewImagesDuration;
    public bool ShowClock => S.ShowClock;
    public string? ClockFormat => S.ClockFormat;
    public string? ClockDateFormat => S.ClockDateFormat;
    public bool ShowProgressBar => S.ShowProgressBar;
    public bool ShowPhotoDate => S.ShowPhotoDate;
    public string? PhotoDateFormat => S.PhotoDateFormat;
    public bool ShowImageDesc => S.ShowImageDesc;
    public bool ShowPeopleDesc => S.ShowPeopleDesc;
    public bool ShowTagsDesc => S.ShowTagsDesc;
    public bool ShowAlbumName => S.ShowAlbumName;
    public bool ShowImageLocation => S.ShowImageLocation;
    public string? ImageLocationFormat => S.ImageLocationFormat;
    public string? PrimaryColor => S.PrimaryColor;
    public string? SecondaryColor => S.SecondaryColor;
    public string Style => S.Style;
    public string? BaseFontSize => S.BaseFontSize;
    public bool ShowWeatherDescription => S.ShowWeatherDescription;
    public string? WeatherIconUrl => S.WeatherIconUrl;
    public bool ImageZoom => S.ImageZoom;
    public bool ImagePan => S.ImagePan;
    public bool ImageFill => S.ImageFill;
    public bool PlayAudio => S.PlayAudio;
    public string Layout => S.Layout;
    public string Language => S.Language;

    public void Validate() => S.Validate();
}
