using UrubuApi.DTOs;

namespace UrubuApi.Services.Orchestrator;

public interface IHunterOrchestrator
{
    HuntStatusDto GetStatus();
    Task<HuntStatusDto> TriggerHuntAsync(string domain, IReadOnlyList<string>? targetStores = null, CancellationToken ct = default);
}
