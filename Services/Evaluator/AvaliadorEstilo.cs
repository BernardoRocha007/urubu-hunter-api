using UrubuApi.Models;

namespace UrubuApi.Services.Evaluator;

public class AvaliadorEstilo : IAvaliadorEstilo
{
    public bool PassaNoPreFiltro(Product produto, StyleProfile perfil)
    {
        if (produto.Price <= 0m) return false;

        // Teto de preço com tolerância de 50% para peças premium (jaquetas, casacos, alfaiataria)
        var tetoPreco = perfil.MaxPrice * 1.5m;
        if (produto.Price > tetoPreco) return false;

        // Termos proibidos (ex: skinny, poliéster)
        if (perfil.ForbiddenTerms != null && perfil.ForbiddenTerms.Count > 0)
        {
            var texto = $"{produto.Title} {string.Join(" ", produto.Tags)} {produto.Category}".ToLowerInvariant();
            foreach (var proibido in perfil.ForbiddenTerms)
            {
                if (!string.IsNullOrWhiteSpace(proibido) && texto.Contains(proibido.Trim().ToLowerInvariant()))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public EvaluationResult Avaliar(Product produto, StyleProfile perfil)
    {
        var pontuacao = 50;
        var motivosCompatibilidade = new List<string>();
        var ressalvas = new List<string>();
        var tagsDetectadas = new List<string>();

        var texto = $"{produto.Title} {string.Join(" ", produto.Tags)} {produto.Category}".ToLowerInvariant();

        // 1. Compatibilidade estética (Keywords)
        var matchesPalavrasChave = 0;
        if (perfil.AestheticKeywords != null)
        {
            foreach (var kw in perfil.AestheticKeywords)
            {
                if (!string.IsNullOrWhiteSpace(kw) && texto.Contains(kw.Trim().ToLowerInvariant()))
                {
                    matchesPalavrasChave++;
                    tagsDetectadas.Add(kw.Trim().ToLowerInvariant());
                }
            }
        }

        if (matchesPalavrasChave > 0)
        {
            var bonus = Math.Min(30, matchesPalavrasChave * 15);
            pontuacao += bonus;
            motivosCompatibilidade.Add($"Estilo compatível ({string.Join(", ", tagsDetectadas.Take(3))})");
        }

        // 2. Desconto ativo
        if (produto.DiscountPct.HasValue && produto.DiscountPct.Value > 0)
        {
            if (produto.DiscountPct.Value >= 40)
            {
                pontuacao += 20;
                motivosCompatibilidade.Add($"Super desconto de {produto.DiscountPct.Value}% OFF");
            }
            else if (produto.DiscountPct.Value >= 15)
            {
                pontuacao += 10;
                motivosCompatibilidade.Add($"Desconto ativo de {produto.DiscountPct.Value}% OFF");
            }
        }

        // 3. Orçamento e Preço
        if (produto.Price <= perfil.MaxPrice)
        {
            pontuacao += 8;
            motivosCompatibilidade.Add($"Preço dentro do orçamento planejado (R$ {produto.Price:N2})");
        }
        else if (produto.Price > perfil.MaxPrice)
        {
            var excedentePct = (int)Math.Round(((produto.Price - perfil.MaxPrice) / perfil.MaxPrice) * 100m);
            pontuacao -= Math.Min(25, excedentePct / 2);
            ressalvas.Add($"Preço {excedentePct}% acima do limite padrão (R$ {perfil.MaxPrice:N2})");
        }

        // 4. Tamanhos disponíveis
        if (perfil.PreferredSizes != null && perfil.PreferredSizes.Count > 0 && produto.AvailableSizes != null && produto.AvailableSizes.Count > 0)
        {
            var tamanhoDisponivel = perfil.PreferredSizes.Any(ps =>
                produto.AvailableSizes.Any(asize => asize.Equals(ps, StringComparison.OrdinalIgnoreCase) || asize.Contains(ps, StringComparison.OrdinalIgnoreCase)));

            if (tamanhoDisponivel)
            {
                pontuacao += 10;
                motivosCompatibilidade.Add("Disponível nos seus tamanhos ideais");
            }
            else if (!produto.AvailableSizes.Contains("Consultar Loja"))
            {
                pontuacao -= 15;
                ressalvas.Add("Tamanhos preferidos aparentemente esgotados");
            }
        }

        pontuacao = Math.Clamp(pontuacao, 0, 100);

        var veredito = pontuacao switch
        {
            >= 85 => "Match Perfeito",
            >= 70 => "Boa Oportunidade",
            >= 50 => "Razoável",
            _ => "Pouca Aderência"
        };

        return new EvaluationResult
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = produto.Id,
            Score = pontuacao,
            Verdict = veredito,
            MatchReasons = motivosCompatibilidade,
            Concerns = ressalvas,
            DetectedTags = tagsDetectadas,
            IsRecommended = pontuacao >= 70,
            EvaluatedAt = DateTime.UtcNow
        };
    }
}
