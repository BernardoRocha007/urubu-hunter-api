namespace UrubuApi.Models;

public class StyleProfile
{
    public string Domain { get; set; } = "fashion"; // PK
    public List<string> AestheticKeywords { get; set; } = new();
    public List<string> PreferredSizes { get; set; } = new();
    public decimal MaxPrice { get; set; } = 400m;
    public int? MinDiscountPct { get; set; } = 0;
    public List<string> ForbiddenTerms { get; set; } = new();
    public List<string> TargetStores { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
