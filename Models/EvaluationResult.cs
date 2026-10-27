namespace UrubuApi.Models;

public class EvaluationResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ItemId { get; set; } = string.Empty;
    public int Score { get; set; } // 0 - 100
    public List<string> MatchReasons { get; set; } = new();
    public List<string> Concerns { get; set; } = new();
    public List<string> DetectedTags { get; set; } = new();
    public string Verdict { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}
