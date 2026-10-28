using UrubuApi.Models;

namespace UrubuApi.Services.Evaluator;

public class StyleEvaluator : IStyleEvaluator
{
    public bool PassesPreFilter(Product product, StyleProfile profile)
    {
        if (product.Price <= 0m) return false;

        // Teto de preço com tolerância de 50% para peças premium (jaquetas, casacos, alfaiataria)
        var priceCeiling = profile.MaxPrice * 1.5m;
        if (product.Price > priceCeiling) return false;

        // Termos proibidos (ex: skinny, poliéster)
        if (profile.ForbiddenTerms != null && profile.ForbiddenTerms.Count > 0)
        {
            var text = $"{product.Title} {string.Join(" ", product.Tags)} {product.Category}".ToLowerInvariant();
            foreach (var forbidden in profile.ForbiddenTerms)
            {
                if (!string.IsNullOrWhiteSpace(forbidden) && text.Contains(forbidden.Trim().ToLowerInvariant()))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public EvaluationResult Evaluate(Product product, StyleProfile profile)
    {
        var score = 50;
        var matchReasons = new List<string>();
        var concerns = new List<string>();
        var detectedTags = new List<string>();

        var text = $"{product.Title} {string.Join(" ", product.Tags)} {product.Category}".ToLowerInvariant();

        // 1. Compatibilidade estética (Keywords)
        var keywordMatches = 0;
        if (profile.AestheticKeywords != null)
        {
            foreach (var kw in profile.AestheticKeywords)
            {
                var cleanKw = kw.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(cleanKw) && text.Contains(cleanKw))
                {
                    keywordMatches++;
                    detectedTags.Add(cleanKw);
                }
            }
        }

        if (keywordMatches > 0)
        {
            var bonus = Math.Min(35, keywordMatches * 10);
            score += bonus;
            matchReasons.Add($"Estilo compatível ({string.Join(", ", detectedTags.Take(3))})");
        }

        // 2. Desconto
        if (product.DiscountPct.HasValue && product.DiscountPct.Value > 0)
        {
            var discount = product.DiscountPct.Value;
            var minDiscount = profile.MinDiscountPct ?? 0;

            if (discount >= minDiscount)
            {
                score += discount >= 30 ? 20 : 12;
                matchReasons.Add($"Desconto ativo de {discount}% OFF");
            }
        }

        // 3. Preço dentro do teto estrito
        if (product.Price <= profile.MaxPrice)
        {
            score += 10;
            matchReasons.Add($"Preço dentro do orçamento planejado (R$ {product.Price:F2})");
        }
        else
        {
            score -= 15;
            concerns.Add($"Valor acima do orçamento padrão (R$ {product.Price:F2} vs teto de R$ {profile.MaxPrice:F2})");
        }

        // 4. Disponibilidade de tamanhos
        if (profile.PreferredSizes != null && profile.PreferredSizes.Count > 0 && product.AvailableSizes.Count > 0)
        {
            var hasMatchingSize = product.AvailableSizes.Any(s =>
                profile.PreferredSizes.Any(ps => s.Equals(ps, StringComparison.OrdinalIgnoreCase)));

            if (hasMatchingSize)
            {
                score += 8;
                matchReasons.Add("Disponível nos seus tamanhos ideais");
            }
        }

        // Clamping final
        score = Math.Clamp(score, 15, 98);

        var verdict = score >= 85 ? "Match Perfeito" :
                      score >= 70 ? "Boa Oportunidade" : "Razoável";

        return new EvaluationResult
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = product.Id,
            Score = score,
            MatchReasons = matchReasons,
            Concerns = concerns,
            DetectedTags = detectedTags,
            Verdict = verdict,
            IsRecommended = score >= 75,
            EvaluatedAt = DateTime.UtcNow
        };
    }
}
