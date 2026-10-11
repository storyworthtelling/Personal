namespace Pulp.Tests.Infrastructure;

/// <summary>
/// Invented responses in the shape of the Streaming Availability API v4 OpenAPI schemas
/// (<c>show</c>, <c>streamingOption</c>, <c>season</c>, <c>episode</c>, <c>changesResult</c>):
/// https://github.com/movieofthenight/streaming-availability-api/blob/main/openapi.yaml.
/// Every title, id and link is made up.
/// </summary>
internal static class StreamingAvailabilityFixtures
{
    public const string Series = """
    {
      "itemType": "show",
      "showType": "series",
      "id": "70001",
      "imdbId": "tt9900001",
      "tmdbId": "tv/990001",
      "title": "The Lantern Keepers",
      "originalTitle": "The Lantern Keepers",
      "overview": "Two keepers of a failing lighthouse trade letters with a ship that never docks.",
      "firstAirYear": 2024,
      "lastAirYear": 2025,
      "genres": [ { "id": "drama", "name": "Drama" }, { "id": "mystery", "name": "Mystery" } ],
      "creators": [ "Invented Creator" ],
      "cast": [ "Invented Actor" ],
      "rating": 81,
      "seasonCount": 2,
      "episodeCount": 3,
      "imageSet": {
        "verticalPoster": {
          "w240": "https://example.com/poster/lantern-240.jpg",
          "w360": "https://example.com/poster/lantern-360.jpg",
          "w480": "https://example.com/poster/lantern-480.jpg",
          "w600": "https://example.com/poster/lantern-600.jpg",
          "w720": "https://example.com/poster/lantern-720.jpg"
        },
        "horizontalPoster": {
          "w360": "https://example.com/wide/lantern-360.jpg",
          "w480": "https://example.com/wide/lantern-480.jpg",
          "w720": "https://example.com/wide/lantern-720.jpg",
          "w1080": "https://example.com/wide/lantern-1080.jpg",
          "w1440": "https://example.com/wide/lantern-1440.jpg"
        }
      },
      "streamingOptions": {
        "us": [
          {
            "service": { "id": "netflix", "name": "Netflix", "homePage": "https://example.com/n" },
            "type": "subscription",
            "link": "https://example.com/n/title/70001",
            "videoLink": "https://example.com/n/watch/70001",
            "quality": "uhd",
            "audios": [ { "language": "eng" } ],
            "subtitles": [],
            "expiresSoon": true,
            "expiresOn": 1793491200,
            "availableSince": 1700000000
          },
          {
            "service": { "id": "corner", "name": "Corner Video Store", "homePage": "https://example.com/c" },
            "type": "buy",
            "link": "https://example.com/c/title/70001",
            "quality": "hd",
            "audios": [],
            "subtitles": [],
            "price": { "amount": "24.99", "currency": "USD", "formatted": "24.99 USD" },
            "expiresSoon": false,
            "availableSince": 1700000000
          }
        ]
      },
      "seasons": [
        {
          "itemType": "season",
          "title": "Season 1",
          "firstAirYear": 2024,
          "lastAirYear": 2024,
          "streamingOptions": { "us": [] },
          "episodes": [
            {
              "itemType": "episode",
              "title": "Lamplight",
              "overview": "The first letter arrives soaked.",
              "airYear": 2024,
              "streamingOptions": {
                "us": [
                  {
                    "service": { "id": "netflix", "name": "Netflix", "homePage": "https://example.com/n" },
                    "type": "subscription",
                    "link": "https://example.com/n/title/70001/1/1",
                    "videoLink": "https://example.com/n/watch/70001-1-1",
                    "quality": "qhd",
                    "audios": [],
                    "subtitles": [],
                    "expiresSoon": false,
                    "availableSince": 1700000000
                  }
                ]
              }
            },
            {
              "itemType": "episode",
              "title": "Tidewater",
              "overview": "A reply is written in pencil.",
              "airYear": 2024,
              "streamingOptions": { "us": [] }
            }
          ]
        },
        {
          "itemType": "season",
          "title": "Season 2",
          "firstAirYear": 2025,
          "lastAirYear": 2025,
          "streamingOptions": { "us": [] },
          "episodes": [
            {
              "itemType": "episode",
              "title": "Low Bridge",
              "overview": "The ship is seen from the shore.",
              "airYear": 2025,
              "streamingOptions": { "us": [] }
            }
          ]
        }
      ]
    }
    """;

    public const string Film = """
    {
      "itemType": "show",
      "showType": "movie",
      "id": "70002",
      "imdbId": "tt9900002",
      "tmdbId": "movie/990002",
      "title": "Harbour of Small Hours",
      "originalTitle": "Harbour of Small Hours",
      "overview": "A night ferry runs late on purpose.",
      "releaseYear": 2023,
      "genres": [ { "id": "romance", "name": "Romance" } ],
      "directors": [ "Invented Director" ],
      "cast": [],
      "rating": 70,
      "runtime": 104,
      "imageSet": {
        "verticalPoster": { "w240": "https://example.com/p/h-240.jpg", "w360": "https://example.com/p/h-360.jpg", "w480": "https://example.com/p/h-480.jpg", "w600": "https://example.com/p/h-600.jpg", "w720": "https://example.com/p/h-720.jpg" },
        "horizontalPoster": { "w360": "https://example.com/w/h-360.jpg", "w480": "https://example.com/w/h-480.jpg", "w720": "https://example.com/w/h-720.jpg", "w1080": "https://example.com/w/h-1080.jpg", "w1440": "https://example.com/w/h-1440.jpg" }
      },
      "streamingOptions": {
        "us": [
          {
            "service": { "id": "driftwood", "name": "Driftwood Free", "homePage": "https://example.com/d" },
            "type": "free",
            "link": "https://example.com/d/70002",
            "quality": "sd",
            "audios": [],
            "subtitles": [],
            "expiresSoon": false,
            "availableSince": 1700000000
          },
          {
            "service": { "id": "prime", "name": "Prime Video", "homePage": "https://example.com/p" },
            "type": "addon",
            "addon": { "id": "invented-channel", "name": "Invented Channel", "homePage": "https://example.com/ic" },
            "link": "https://example.com/p/70002",
            "quality": "hd",
            "audios": [],
            "subtitles": [],
            "expiresSoon": false,
            "availableSince": 1700000000
          },
          {
            "service": { "id": "corner", "name": "Corner Video Store", "homePage": "https://example.com/c" },
            "type": "rent",
            "link": "https://example.com/c/70002",
            "quality": "hd",
            "audios": [],
            "subtitles": [],
            "price": { "amount": "3.99", "currency": "USD", "formatted": "3.99 USD" },
            "expiresSoon": false,
            "availableSince": 1700000000
          }
        ]
      }
    }
    """;

    public static string SearchResults => "[" + Film + "," + Series + "]";

    public static string Changes => """
    {
      "changes": [
        {
          "changeType": "expiring",
          "itemType": "show",
          "showId": "70002",
          "showType": "movie",
          "service": { "id": "hulu", "name": "Hulu", "homePage": "https://example.com/h" },
          "streamingOptionType": "subscription",
          "timestamp": 1793491200,
          "link": "https://example.com/h/70002"
        },
        {
          "changeType": "expiring",
          "itemType": "episode",
          "showId": "70001",
          "showType": "series",
          "season": 1,
          "episode": 2,
          "service": { "id": "netflix", "name": "Netflix", "homePage": "https://example.com/n" },
          "streamingOptionType": "subscription"
        }
      ],
      "shows": {
        "70002": FILM,
        "70001": SERIES
      },
      "hasMore": true,
      "nextCursor": "70001:invented"
    }
    """.Replace("FILM", Film).Replace("SERIES", Series);
}
