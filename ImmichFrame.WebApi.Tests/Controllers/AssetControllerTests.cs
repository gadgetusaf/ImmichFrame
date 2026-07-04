using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http; // Added this
using Moq;
using Moq.Protected;
using ImmichFrame.Core.Api;
using ImmichFrame.WebApi.Controllers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.Core.Interfaces; // Added this back
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers
{
    [TestFixture]
    public class AssetControllerTests
    {
        private WebApplicationFactory<Program> _factory;
        private Mock<HttpMessageHandler> _mockHttpMessageHandler;

        [SetUp]
        public void Setup()
        {
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        // 1. Mock HttpMessageHandler and IHttpClientFactory
                        services.AddSingleton<HttpMessageHandler>(_mockHttpMessageHandler.Object);
                        services.AddHttpClient("ImmichApiAccountClient")
                            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<HttpMessageHandler>());
                        services.ConfigureAll<HttpClientFactoryOptions>(options =>
                        {
                            options.HttpMessageHandlerBuilderActions.Add(b =>
                            {
                                b.PrimaryHandler = b.Services.GetRequiredService<HttpMessageHandler>();
                            });
                        });

                        // 2. Directly instantiate and register settings objects
                        var generalSettings = new GeneralSettings
                        {
                            ShowWeatherDescription = false, // Assuming this corresponds to ShowWeather
                            WeatherIconUrl = "https://openweathermap.org/img/wn/{IconId}.png",
                            ShowClock = true,
                            ClockFormat = "HH:mm",
                            ClockDateFormat = "eee, MMM d",
                            Language = "en",
                            PhotoDateFormat = "MM/dd/yyyy", // Crucial for the NRE
                            ImageLocationFormat = "City,State,Country",
                            DownloadImages = false,
                            RenewImagesDuration = 30,
                            // Ensure all non-nullable string properties that might be used have defaults if not set here
                            PrimaryColor = "#FFFFFF", // Example default
                            SecondaryColor = "#000000", // Example default
                            Style = "none",
                            BaseFontSize = "16px",
                            WeatherApiKey = "",
                            UnitSystem = "imperial",
                            WeatherLatLong = "0,0"
                        };

                        var accountSettings = new ServerAccountSettings
                        {
                            ImmichServerUrl = "http://mock-immich-server.com",
                            ApiKey = "test-api-key",
                            ShowMemories = false,
                            ShowFavorites = true,
                            ShowArchived = false,
                            Albums = new List<Guid>(),
                            ExcludedAlbums = new List<Guid>(),
                            People = new List<Guid>()
                        };

                        var serverSettings = new ServerSettings
                        {
                            GeneralSettingsImpl = generalSettings,
                            AccountsImpl = new List<ServerAccountSettings> { accountSettings }
                        };

                        services.AddSingleton<IServerSettings>(serverSettings);
                        services.AddSingleton<IGeneralSettings>(generalSettings);
                        // Ensure IAccountSettings can be resolved if needed by MultiImmichFrameLogicDelegate directly
                        // However, PooledImmichFrameLogic receives IAccountSettings via the factory Func
                    });
                });
        }

        // Removed OneTimeSetup that created Settings.json

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
        }

        [Test]
        public async Task GetRandomImage_ReturnsImageFromMockServer()
        {
            // Arrange
            var expectedAssetId = Guid.NewGuid();
            var assetDtoJson = $@"
            {{
                ""id"": ""{expectedAssetId}"",
                ""originalPath"": ""/path/to/image.jpg"",
                ""type"": ""IMAGE"",
                ""createdAt"": ""2023-10-26T10:00:00Z"",
                ""fileCreatedAt"": ""2023-10-26T10:00:00Z"",
                ""fileModifiedAt"": ""2023-10-26T10:00:00Z"",
                ""isEdited"": false,
                ""isFavorite"": true,
                ""duration"": null,
                ""checksum"": ""testchecksum"",
                ""deviceAssetId"": ""testDeviceAssetId"",
                ""deviceId"": ""testDeviceId"",
                ""ownerId"": ""00000000-0000-0000-0000-0000000000aa"",
                ""originalFileName"": ""image.jpg"",
                ""localDateTime"": ""2023-10-26T10:00:00Z"",
                ""visibility"": ""timeline"",
                ""hasMetadata"": true,
                ""isArchived"": false,
                ""isOffline"": false,
                ""isTrashed"": false,
                ""thumbhash"": ""I0cMCQS94XmImZeXmYd3d3g="",
                ""width"": null,
                ""height"": null,
                ""updatedAt"": ""2023-10-26T10:00:00Z""
            }}";

            // JSON structure for SearchResponseDto
            var jsonResponse = $@"
            {{
                ""albums"": {{
                    ""count"": 0,
                    ""items"": [],
                    ""total"": 0,
                    ""facets"": []
                }},
                ""assets"": {{
                    ""count"": 1,
                    ""items"": [
                        {assetDtoJson}
                    ],
                    ""total"": 1,
                    ""facets"": [],
                    ""nextPage"": null
                }}
            }}";

            // Setup for SearchAssetsAsync
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/search/metadata")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() => new HttpResponseMessage // Use a Func to return new instance each time
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse)
                });

            // Setup for ViewAssetAsync (thumbnail)
            var mockImageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // Minimal JPEG
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/thumbnail")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() => new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(mockImageData)
                });

            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo");

            // Assert
            response.EnsureSuccessStatusCode();
            var image = await response.Content.ReadFromJsonAsync<ImageResponse>();
            Assert.That(image, Is.Not.Null);

            // The endpoint must return the actual bytes served by the mocked Immich thumbnail
            // endpoint, base64-encoded — not empty/placeholder data.
            var decoded = Convert.FromBase64String(image!.RandomImageBase64);
            Assert.That(decoded, Is.EqualTo(mockImageData), "RandomImageBase64 must decode to the mocked JPEG bytes");

            // PhotoDate is formatted from the asset's localDateTime (2023-10-26) using the configured
            // PhotoDateFormat (MM/dd/yyyy) and Language (en).
            Assert.That(image.PhotoDate, Is.EqualTo("10/26/2023"));

            // No EXIF location on the mocked asset => empty ImageLocation (not the literal format string).
            Assert.That(image.ImageLocation, Is.Empty);
        }
    }
}
