namespace UrubuApi.Models;

public class Product
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Domain { get; set; } = "fashion";
    public string Title { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OriginalPrice { get; set; }
    public int? DiscountPct { get; set; }
    public string Url { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public List<string> AvailableSizes { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Source { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
