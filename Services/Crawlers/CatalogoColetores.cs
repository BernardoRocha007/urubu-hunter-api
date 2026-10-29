namespace UrubuApi.Services.Crawlers;

public class CatalogoColetores
{
    private readonly IEnumerable<IColetorSite> _coletores;
    private readonly ColetorUniversal _coletorUniversal;

    public CatalogoColetores(IEnumerable<IColetorSite> coletores, ColetorUniversal coletorUniversal)
    {
        _coletores = coletores;
        _coletorUniversal = coletorUniversal;
    }

    public IColetorSite ObterColetorParaUrl(string url)
    {
        var correspondente = _coletores.FirstOrDefault(s => s is not ColetorUniversal && s.PodeProcessar(url));
        return correspondente ?? _coletorUniversal;
    }
}
