namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Previsão do split de cada inscrito enquanto o evento acontece: o RaceOS não diz quem caiu em qual split antes do
/// resultado, mas a configuração das diárias manda dividir por igual ("splitSetting": "even"). A hipótese é a ordem do
/// DR (elo) do maior para o menor, em fatias do mesmo tamanho. Só vale mostrar se o acerto medido contra os
/// resultados reais for alto (comando check-splits do Worker).
/// </summary>
public static class LiveSplits
{
    public enum Strategy
    {
        /// <summary>Todos juntos pela ordem de elo; as primeiras fatias levam o resto da divisão.</summary>
        EvenFrontLoaded,
        /// <summary>Todos juntos pela ordem de elo; as últimas fatias levam o resto da divisão.</summary>
        EvenBackLoaded,
        /// <summary>Cada classe em separado pela ordem de elo, repartida por igual entre os splits.</summary>
        PerClass,
    }

    /// <summary>Split previsto (1..splits) de cada inscrito, na mesma ordem da lista. Null onde falta a posição de elo.</summary>
    public static int?[] Predict(IReadOnlyList<LiveDetail.Entry> entries, int splits, Strategy strategy = Strategy.EvenFrontLoaded)
    {
        var result = new int?[entries.Count];
        if (splits < 1) return result;
        var ranked = Enumerable.Range(0, entries.Count).Where(i => entries[i].EloPos is not null).ToList();

        if (strategy == Strategy.PerClass)
        {
            foreach (var g in ranked.GroupBy(i => entries[i].CarClass ?? ""))
                Slice(g.OrderBy(i => entries[i].EloPos).ToList(), splits, frontLoaded: true, result);
            return result;
        }
        Slice(ranked.OrderBy(i => entries[i].EloPos).ToList(), splits, strategy == Strategy.EvenFrontLoaded, result);
        return result;
    }

    /// <summary>Divide a lista ordenada em <paramref name="splits"/> fatias de tamanho igual (diferença máxima de 1).</summary>
    private static void Slice(List<int> ordered, int splits, bool frontLoaded, int?[] result)
    {
        var n = ordered.Count;
        var size = n / splits;
        var extra = n % splits;
        var pos = 0;
        for (var s = 1; s <= splits && pos < n; s++)
        {
            var take = size + (frontLoaded ? (s <= extra ? 1 : 0) : (s > splits - extra ? 1 : 0));
            for (var k = 0; k < take && pos < n; k++) result[ordered[pos++]] = s;
        }
    }
}
