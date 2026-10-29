using UrubuApi.DTOs;

namespace UrubuApi.Services.Orchestrator;

public interface IOrquestradorGarimpo
{
    HuntStatusDto ObterStatus();
    Task<HuntStatusDto> IniciarGarimpoAsync(string dominio, IReadOnlyList<string>? lojasAlvo = null, CancellationToken ct = default);
}
