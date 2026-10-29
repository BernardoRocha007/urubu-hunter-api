using UrubuApi.Models;

namespace UrubuApi.Services.Evaluator;

public interface IAvaliadorEstilo
{
    bool PassaNoPreFiltro(Product produto, StyleProfile perfil);
    EvaluationResult Avaliar(Product produto, StyleProfile perfil);
}
