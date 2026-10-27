namespace UrubuApi.Models;

public class CuratedItem : Product
{
    public EvaluationResult? Evaluation { get; set; }
    public string? Feedback { get; set; } // "liked", "disliked", "saved", null
    public DateTime CuratedAt { get; set; } = DateTime.UtcNow;
}
