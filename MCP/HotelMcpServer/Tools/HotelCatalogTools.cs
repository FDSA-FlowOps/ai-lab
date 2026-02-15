using System.ComponentModel;
using ModelContextProtocol.Server;

internal sealed class HotelCatalogTools
{
    private static readonly Hotel[] Hotels =
    [
        new("Gran Via Palace", "Luxury", "Madrid", "Spain", 5, 265, ["Spa", "Rooftop", "Gym"]),
        new("Sol Atocha", "Business", "Madrid", "Spain", 4, 180, ["Coworking", "Meeting Rooms"]),
        new("Rambla Breeze", "Budget", "Barcelona", "Spain", 3, 95, ["Breakfast", "WiFi"]),
        new("Seine Garden", "Luxury", "Paris", "France", 5, 320, ["Spa", "Michelin Restaurant"]),
        new("Louvre Connect", "Business", "Paris", "France", 4, 210, ["Coworking", "Airport Transfer"]),
        new("Tagus View", "Boutique", "Lisbon", "Portugal", 4, 140, ["Terrace", "Breakfast"])
    ];

    private static readonly PlaceDetails[] Places =
    [
        new("Madrid", "Spain", "Capital of Spain. Known for museums, parks, and historical neighborhoods."),
        new("Barcelona", "Spain", "Mediterranean city famous for modernist architecture and beaches."),
        new("Paris", "France", "Capital of France. Major center of art, cuisine, and culture."),
        new("Lisbon", "Portugal", "Capital of Portugal. Known for hills, trams, and Atlantic views.")
    ];

    [McpServerTool]
    [Description("Returns static hotels and allows filtering by category and/or place.")]
    public IReadOnlyList<Hotel> GetHotels(
        [Description("Hotel category filter, e.g. Luxury, Business, Budget, Boutique")] string? category = null,
        [Description("Place filter, e.g. Madrid, Paris")] string? place = null)
    {
        IEnumerable<Hotel> query = Hotels;

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(h => string.Equals(h.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(place))
        {
            query = query.Where(h => string.Equals(h.Place, place, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToArray();
    }

    [McpServerTool]
    [Description("Returns static place details and allows filtering by country and/or place name.")]
    public IReadOnlyList<PlaceDetails> GetPlaceDetails(
        [Description("Country filter, e.g. Spain, France")] string? country = null,
        [Description("Place name filter, e.g. Madrid")] string? place = null)
    {
        IEnumerable<PlaceDetails> query = Places;

        if (!string.IsNullOrWhiteSpace(country))
        {
            query = query.Where(p => string.Equals(p.Country, country, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(place))
        {
            query = query.Where(p => string.Equals(p.Name, place, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToArray();
    }
}

internal sealed record Hotel(
    string Name,
    string Category,
    string Place,
    string Country,
    int Stars,
    decimal PricePerNightEur,
    string[] Amenities);

internal sealed record PlaceDetails(
    string Name,
    string Country,
    string Details);
