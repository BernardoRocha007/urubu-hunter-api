using UrubuApi.Models;

namespace UrubuApi.Services.Evaluator;

public interface IStyleEvaluator
{
    bool PassesPreFilter(Product product, StyleProfile profile);
    EvaluationResult Evaluate(Product product, StyleProfile profile);
}
