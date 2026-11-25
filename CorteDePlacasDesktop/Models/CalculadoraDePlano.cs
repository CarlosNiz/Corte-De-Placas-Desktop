using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CorteDePlacasDesktop.Models;

/// <summary>
/// Calcula o plano de corte: em quantas chapas as peças cabem e onde fica cada uma.
/// Testa muitas formas de montar o plano e fica com a melhor:
/// <list type="bullet">
/// <item>várias ordens das peças (maiores primeiro por área, pelo lado maior, pelo menor... e algumas embaralhadas);</item>
/// <item>várias regras de onde colocar cada peça e, na serra, de qual corte fazer primeiro;</item>
/// <item>dois jeitos de encher as chapas: cada estratégia do começo ao fim, ou chapa por chapa,
/// escolhendo a cada chapa a estratégia que mais aproveita aquela chapa.</item>
/// </list>
/// Melhor = menos peças de fora, depois menos chapas, depois a última chapa mais vazia
/// (as sobras juntas no fim), depois a maior sobra inteira na última chapa e, na serra, menos cortes.
/// </summary>
public static class CalculadoraDePlano
{
    private const double Eps = 1e-6;

    /// <summary>Quantas ordens "embaralhadas" (maiores primeiro, com um pouco de sorteio) testar além das fixas.</summary>
    private const int OrdensEmbaralhadas = 8;

    /// <summary>Uma unidade de peça a colocar.</summary>
    private sealed record Item(PecaPedida Peca, int Copia)
    {
        public double W => Peca.Comprimento;
        public double H => Peca.Largura;
    }

    /// <summary>Como ficou uma chapa: as peças que entraram e (na serra) os cortes.</summary>
    private sealed record Enchimento(List<(Item Item, Retangulo Area, bool Girada)> Colocadas, List<CorteSerra> Cortes)
    {
        public double Area => Colocadas.Sum(c => c.Item.W * c.Item.H);

        /// <summary>Maior sobra inteira da chapa (calculada só quando precisa desempatar).</summary>
        public double? Sobra { get; set; }
    }

    private sealed record Tentativa(List<ChapaDoPlano> Chapas, List<Item> Sobraram)
    {
        /// <summary>Maior sobra inteira da última chapa (calculada só quando precisa desempatar).</summary>
        public double? SobraDaUltima { get; set; }
    }

    private static readonly Func<Item, double>[] OrdensFixas =
    [
        i => i.W * i.H,
        i => Math.Max(i.W, i.H),
        i => Math.Min(i.W, i.H),
        i => i.W + i.H,
        i => i.W,
        i => i.H,
    ];

    /// <summary>
    /// Calcula o melhor plano. Testa as duas arrumações e fica com a melhor:
    /// <list type="bullet">
    /// <item>cortes retos de ponta a ponta (<see cref="TipoCorte.Serra"/>): serve para serra e para CNC e tem a ordem dos cortes;</item>
    /// <item>encaixe livre (<see cref="TipoCorte.Cnc"/>): a fresa contorna cada peça, que podem se encostar pela ponta.</item>
    /// </list>
    /// O encaixe livre só ganha se deixar menos peças de fora ou usar menos chapas; no empate ficam os cortes retos.
    /// </summary>
    public static ResultadoPlano Calcular(FormatoChapa chapa, double refilo, double espessura, IReadOnlyList<PecaPedida> pecas) =>
        Calcular(chapa, refilo, espessura, null, pecas);

    /// <summary>Calcula o plano só com uma das arrumações (ou com as duas, se <paramref name="tipo"/> for null).</summary>
    public static ResultadoPlano Calcular(FormatoChapa chapa, double refilo, double espessura, TipoCorte? tipo,
        IReadOnlyList<PecaPedida> pecas)
    {
        var resultado = CalcularPlano(chapa, refilo, espessura, tipo, pecas);

        // Mais de uma chapa: ver se é a espessura do corte que impede mais peças por chapa
        // (peças que somam a medida exata da chapa não cabem lado a lado, porque o corte gasta material).
        if (resultado.Chapas.Count > 1 && espessura > 0 && resultado.NaoCouberam.Count == 0
            && pecas.All(p => Math.Min(p.Comprimento, p.Largura) > 2 * espessura))
        {
            var menores = pecas
                .Select(p => p with { Comprimento = p.Comprimento - espessura, Largura = p.Largura - espessura })
                .ToList();
            var comMenores = CalcularPlano(chapa, refilo, espessura, tipo, menores);
            if (comMenores.NaoCouberam.Count == 0 && comMenores.Chapas.Count < resultado.Chapas.Count)
                resultado = resultado with { ChapasComPecasMenores = comMenores.Chapas.Count };
        }
        return resultado;
    }

    private static ResultadoPlano CalcularPlano(FormatoChapa chapa, double refilo, double k, TipoCorte? tipo,
        IReadOnlyList<PecaPedida> pecas)
    {
        if (tipo is { } escolhido)
            return CalcularArrumacao(chapa, refilo, k, escolhido, pecas);

        var retos = CalcularArrumacao(chapa, refilo, k, TipoCorte.Serra, pecas);
        var livre = CalcularArrumacao(chapa, refilo, k, TipoCorte.Cnc, pecas);
        var foraRetos = retos.NaoCouberam.Sum(n => n.QuantidadeQueNaoCoube);
        var foraLivre = livre.NaoCouberam.Sum(n => n.QuantidadeQueNaoCoube);
        if (foraLivre != foraRetos)
            return foraLivre < foraRetos ? livre : retos;
        return livre.Chapas.Count < retos.Chapas.Count ? livre : retos;
    }

    private static ResultadoPlano CalcularArrumacao(FormatoChapa chapa, double refilo, double k, TipoCorte tipo,
        IReadOnlyList<PecaPedida> pecas)
    {
        var util = chapa.AreaUtil(refilo);
        var itens = pecas.SelectMany(p => Enumerable.Range(1, p.Quantidade).Select(c => new Item(p, c))).ToList();

        if (util.Count == 0 || itens.Count == 0)
            return Montar(tipo, chapa, refilo, k, new Tentativa([], itens));

        // Área útil: o retângulo em volta dela e as partes desse retângulo que não servem (fora, furos).
        var limites = Geometria.Limites(util.SelectMany(p => p));
        var fora = Geometria.RetangulosFora(util, limites);

        // Na chapa irregular, um corte cujo um dos lados é só ar (fora da chapa ou furo) não precisa ser feito.
        Func<Retangulo, bool>? soAr = fora.Count == 0 ? null : r => !chapa.TemMaterial(r);
        var preparo = tipo == TipoCorte.Serra ? CortesDePreparo(chapa, limites, refilo) : [];

        // Para cada tipo de peça, o melhor arranjo de várias peças iguais (deitadas e em pé misturadas)
        var padroes = pecas
            .AsParallel().AsOrdered()
            .Select(p => (p.Numero, Padrao: new PadraoDePecasIguais(p.Comprimento, p.Largura, k, limites.Comprimento, limites.Largura)))
            .ToDictionary(x => x.Numero, x => x.Padrao);

        var enchedores = new List<Func<List<Item>, Enchimento>>();
        foreach (var regra in Enum.GetValues<RegraDeEncaixe>())
        {
            if (tipo == TipoCorte.Cnc)
            {
                enchedores.Add(r => EncherChapaLivre(r, limites, fora, k, regra));
                continue;
            }
            foreach (var divisao in Enum.GetValues<RegraDeDivisao>())
                enchedores.Add(r => EncherChapaSerra(r, limites, fora, preparo, k, regra, divisao, soAr));
        }
        if (tipo == TipoCorte.Cnc)
        {
            enchedores.Add(r => EncherChapaLivreEmBlocos(r, limites, fora, k, padroes));
        }
        else
        {
            foreach (var divisao in Enum.GetValues<RegraDeDivisao>())
                enchedores.Add(r => EncherChapaSerraEmBlocos(r, limites, fora, preparo, k, divisao, soAr, padroes));
        }
        var ordens = Ordens(pecas);

        // 1) Cada estratégia (ordem + regras) do começo ao fim
        var combinacoes = OrdensDistintas(itens, ordens)
            .SelectMany(lista => enchedores.Select(encher => (Lista: lista, Encher: encher)))
            .ToList();
        var tentativas = new Tentativa[combinacoes.Count];
        Parallel.For(0, combinacoes.Count, i => tentativas[i] = Simular(combinacoes[i].Lista, combinacoes[i].Encher));
        // Desempate entre planos iguais: a maior sobra inteira (para caber outra peça depois, e a sobra
        // não ficar picada em volta de um furo)
        double Sobra(IEnumerable<Retangulo> pecasNaChapa) => MaiorSobra(pecasNaChapa.ToArray(), limites, fora, k, tipo);

        Tentativa? melhor = null;
        foreach (var t in tentativas)
            melhor = Melhor(melhor, t, Sobra);

        // 2) Chapa por chapa, cada uma com a estratégia que mais a aproveita
        melhor = Melhor(melhor, ChapaPorChapa(itens, ordens, enchedores, Sobra), Sobra);

        // Na CNC, as peças descem e vão para a esquerda o quanto der: a sobra fica junta, em cima e à direita.
        if (tipo == TipoCorte.Cnc)
            melhor = melhor! with { Chapas = melhor.Chapas.Select(c => c with { Pecas = Compactar(c.Pecas, limites, fora, k) }).ToList() };

        // A maior sobra de cada chapa, com a medida, para o operador ver o que ficou livre
        var areaMinima = 0.02 * limites.Comprimento * limites.Largura;
        melhor = melhor! with
        {
            Chapas = melhor.Chapas.Select(c =>
            {
                var sobra = RetanguloDaMaiorSobra(c.Pecas.Select(p => p.Area).ToArray(), limites, fora, k, tipo);
                return sobra is { } s && s.Area >= areaMinima && Math.Min(s.Comprimento, s.Largura) >= 30 ? c with { Sobra = s } : c;
            }).ToList(),
        };

        return Montar(tipo, chapa, refilo, k, melhor);
    }

    // ------------------------------------------------------------------ busca

    /// <summary>
    /// Ordens a testar: as fixas e algumas "maiores primeiro, com sorteio". O sorteio é por tipo de peça
    /// (cópias iguais ficam juntas) e sempre com a mesma semente: o mesmo pedido dá sempre o mesmo plano.
    /// </summary>
    private static List<Func<Item, double>> Ordens(IReadOnlyList<PecaPedida> pecas)
    {
        var ordens = new List<Func<Item, double>>(OrdensFixas);
        var sorteio = new Random(1234);
        for (var n = 0; n < OrdensEmbaralhadas; n++)
        {
            var peso = pecas.ToDictionary(p => p.Numero, _ => 0.6 + 0.8 * sorteio.NextDouble());
            ordens.Add(i => i.W * i.H * peso[i.Peca.Numero]);
        }
        return ordens;
    }

    /// <summary>As listas ordenadas, sem repetir (com um tipo de peça só, todas as ordens dão a mesma lista).</summary>
    private static List<List<Item>> OrdensDistintas(List<Item> itens, List<Func<Item, double>> ordens)
    {
        var vistas = new HashSet<string>();
        var listas = new List<List<Item>>();
        foreach (var ordem in ordens)
        {
            var lista = itens.OrderByDescending(ordem).ThenByDescending(i => i.W * i.H).ThenBy(i => i.Peca.Numero).ToList();
            if (vistas.Add(string.Join(",", lista.Select(i => i.Peca.Numero))))
                listas.Add(lista);
        }
        return listas;
    }

    /// <summary>Enche chapas, uma de cada vez, até acabarem as peças (ou nenhuma caber numa chapa vazia).</summary>
    private static Tentativa Simular(List<Item> ordenados, Func<List<Item>, Enchimento> encher)
    {
        var restantes = new List<Item>(ordenados);
        var chapas = new List<ChapaDoPlano>();
        while (restantes.Count > 0)
        {
            var enchimento = encher(restantes);
            if (enchimento.Colocadas.Count == 0)
                break;
            foreach (var c in enchimento.Colocadas)
                restantes.Remove(c.Item);
            chapas.Add(NovaChapa(chapas.Count + 1, enchimento));
        }
        return new Tentativa(chapas, restantes);
    }

    /// <summary>
    /// Enche uma chapa de cada vez testando todas as estratégias nas peças que faltam e fica, em cada chapa,
    /// com a que coloca mais área de peça (empate: menos cortes).
    /// </summary>
    private static Tentativa ChapaPorChapa(List<Item> itens, List<Func<Item, double>> ordens,
        List<Func<List<Item>, Enchimento>> enchedores, Func<IEnumerable<Retangulo>, double> sobra)
    {
        var restantes = new List<Item>(itens);
        var chapas = new List<ChapaDoPlano>();
        while (restantes.Count > 0)
        {
            var combinacoes = OrdensDistintas(restantes, ordens)
                .SelectMany(lista => enchedores.Select(encher => (Lista: lista, Encher: encher)))
                .ToList();
            var enchimentos = new Enchimento[combinacoes.Count];
            Parallel.For(0, combinacoes.Count, i => enchimentos[i] = combinacoes[i].Encher(combinacoes[i].Lista));

            // Mais área de peça; empate: maior sobra inteira; depois menos cortes
            Enchimento? melhor = null;
            foreach (var e in enchimentos)
            {
                if (melhor is null || e.Area > melhor.Area + 1)
                {
                    melhor = e;
                    continue;
                }
                if (Math.Abs(e.Area - melhor.Area) > 1)
                    continue;
                var sobraMelhor = melhor.Sobra ??= sobra(melhor.Colocadas.Select(c => c.Area));
                var sobraNova = e.Sobra ??= sobra(e.Colocadas.Select(c => c.Area));
                if (Diferente(sobraNova, sobraMelhor) ? sobraNova > sobraMelhor : e.Cortes.Count < melhor.Cortes.Count)
                    melhor = e;
            }
            if (melhor is null || melhor.Colocadas.Count == 0)
                break;
            foreach (var c in melhor.Colocadas)
                restantes.Remove(c.Item);
            chapas.Add(NovaChapa(chapas.Count + 1, melhor));
        }
        return new Tentativa(chapas, restantes);
    }

    private static ChapaDoPlano NovaChapa(int numero, Enchimento enchimento)
    {
        var pecas = enchimento.Colocadas
            .OrderBy(c => Math.Round(c.Area.Y, 3)).ThenBy(c => c.Area.X)
            .Select(c => new PecaNaChapa(c.Item.Peca.Numero, c.Item.Copia, c.Item.Peca.Descricao, c.Area, c.Girada,
                c.Item.Peca.Formato, c.Item.Peca.AreaUnitaria))
            .ToList();
        return new ChapaDoPlano(numero, pecas, enchimento.Cortes, 0);
    }

    private static Tentativa Melhor(Tentativa? atual, Tentativa nova, Func<IEnumerable<Retangulo>, double> sobra)
    {
        if (atual is null)
            return nova;
        if (nova.Sobraram.Count != atual.Sobraram.Count)
            return nova.Sobraram.Count < atual.Sobraram.Count ? nova : atual;
        if (nova.Chapas.Count != atual.Chapas.Count)
            return nova.Chapas.Count < atual.Chapas.Count ? nova : atual;

        // Mesma quantidade de chapas: melhor quando a última está mais vazia (sobra concentrada no fim)
        var ultimaNova = nova.Chapas.Count > 0 ? nova.Chapas[^1].AreaPecas : 0;
        var ultimaAtual = atual.Chapas.Count > 0 ? atual.Chapas[^1].AreaPecas : 0;
        if (Math.Abs(ultimaNova - ultimaAtual) > 1)
            return ultimaNova < ultimaAtual ? nova : atual;

        // Depois: a maior sobra inteira na última chapa (onde ainda caberia outra peça)
        var sobraNova = nova.SobraDaUltima ??= nova.Chapas.Count > 0 ? sobra(nova.Chapas[^1].Pecas.Select(p => p.Area)) : 0;
        var sobraAtual = atual.SobraDaUltima ??= atual.Chapas.Count > 0 ? sobra(atual.Chapas[^1].Pecas.Select(p => p.Area)) : 0;
        if (Diferente(sobraNova, sobraAtual))
            return sobraNova > sobraAtual ? nova : atual;

        var cortesNova = nova.Chapas.Sum(c => c.Cortes.Count);
        var cortesAtual = atual.Chapas.Sum(c => c.Cortes.Count);
        return cortesNova < cortesAtual ? nova : atual;
    }

    /// <summary>Duas sobras são diferentes se a diferença passa de 1% (abaixo disso, conta como empate).</summary>
    private static bool Diferente(double a, double b) => Math.Abs(a - b) > 0.01 * Math.Max(a, b) + 1;

    // ------------------------------------------------------------------ CNC (encaixe livre)
    //
    // Na CNC a fresa contorna cada peça; não há corte reto atravessando a chapa. Por isso o diâmetro da fresa
    // só precisa ficar entre peças "de frente" uma para a outra (lado a lado, ou uma em cima da outra).
    // Peças que só se tocam pela ponta (na diagonal) podem encostar: a fresa passa por um lado da ponta acima
    // de uma peça e pelo outro lado abaixo da outra, sem tirar material de nenhuma das duas.
    // Na conta, cada peça ocupa o seu retângulo mais quatro faixas da largura da fresa coladas nos lados
    // (sem os cantos): uma peça nova não pode cair em cima de nenhuma delas.

    /// <summary>O retângulo da peça e as quatro faixas da fresa em volta dela (sem os cantos).</summary>
    private static IEnumerable<Retangulo> OcupadoNaCnc(Retangulo p, double k)
    {
        yield return p;
        if (k <= 0)
            yield break;
        yield return new Retangulo(p.X - k, p.Y, k, p.Largura);
        yield return new Retangulo(p.Direita, p.Y, k, p.Largura);
        yield return new Retangulo(p.X, p.Y - k, p.Comprimento, k);
        yield return new Retangulo(p.X, p.Topo, p.Comprimento, k);
    }

    private static void OcuparNaCnc(EncaixeLivre encaixe, Retangulo peca, double k)
    {
        foreach (var r in OcupadoNaCnc(peca, k))
            encaixe.Ocupar(r);
    }

    private static Enchimento EncherChapaLivre(List<Item> restantes, Retangulo limites, List<Retangulo> fora, double k,
        RegraDeEncaixe regra)
    {
        var encaixe = new EncaixeLivre(limites, fora);
        var colocadas = new List<(Item, Retangulo, bool)>();
        foreach (var item in restantes)
        {
            if (encaixe.Procurar(item.W, item.H, regra) is not { } lugar)
                continue;
            OcuparNaCnc(encaixe, lugar.Posicao, k);
            colocadas.Add((item, lugar.Posicao, lugar.Girada));
        }
        return new Enchimento(colocadas, []);
    }

    /// <summary>
    /// Enche a chapa por tipo de peça: todas as cópias de um tipo de uma vez, no espaço onde cabem mais delas,
    /// arrumadas do melhor jeito (girando algumas quando isso aproveita a sobra).
    /// </summary>
    private static Enchimento EncherChapaLivreEmBlocos(List<Item> restantes, Retangulo limites, List<Retangulo> fora,
        double k, Dictionary<int, PadraoDePecasIguais> padroes)
    {
        var encaixe = new EncaixeLivre(limites, fora);
        var colocadas = new List<(Item, Retangulo, bool)>();
        foreach (var grupo in restantes.GroupBy(i => i.Peca.Numero))
        {
            var fila = new Queue<Item>(grupo);
            var padrao = padroes[grupo.Key];
            while (fila.Count > 0)
            {
                // O espaço livre onde cabem mais peças deste tipo (empate: o menor espaço)
                Retangulo? melhor = null;
                var melhorN = 0;
                foreach (var livre in encaixe.Livres)
                {
                    var n = Math.Min(fila.Count, padrao.Quantas(livre.Comprimento, livre.Largura));
                    if (n > melhorN || (n == melhorN && n > 0 && livre.Area < melhor!.Value.Area - Eps))
                    {
                        melhor = livre;
                        melhorN = n;
                    }
                }
                if (melhor is not { } espaco
                    || padrao.MenorRegiao(espaco.Comprimento, espaco.Largura, melhorN) is not { } tamanho)
                    break;

                foreach (var (lugar, girada) in padrao.Posicoes(espaco.X, espaco.Y, tamanho.X, tamanho.Y, melhorN))
                {
                    OcuparNaCnc(encaixe, lugar, k);
                    colocadas.Add((fila.Dequeue(), lugar, girada));
                }
            }
        }
        return new Enchimento(colocadas, []);
    }

    /// <summary>
    /// Junta as peças num canto da chapa: testa os quatro cantos e fica com o que deixa a maior sobra inteira
    /// (empate: embaixo à esquerda). Assim a peça não fica solta no meio, parada em cima de um furo.
    /// </summary>
    private static List<PecaNaChapa> Compactar(IReadOnlyList<PecaNaChapa> pecas, Retangulo limites,
        List<Retangulo> fora, double k)
    {
        Retangulo[]? melhor = null;
        var melhorSobra = -1.0;
        foreach (var (espelharX, espelharY) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            // Espelhar a chapa, empurrar para baixo e para a esquerda, e espelhar de volta = empurrar para aquele canto
            Retangulo Espelho(Retangulo r) => new(
                espelharX ? limites.X + limites.Direita - r.Direita : r.X,
                espelharY ? limites.Y + limites.Topo - r.Topo : r.Y,
                r.Comprimento, r.Largura);

            var areas = EmpurrarParaOCanto(pecas.Select(p => Espelho(p.Area)).ToArray(), limites, fora.Select(Espelho).ToList(), k)
                .Select(Espelho)
                .ToArray();
            var sobra = MaiorSobra(areas, limites, fora, k, TipoCorte.Cnc);
            if (sobra > melhorSobra * 1.01 + 1)
            {
                melhor = areas;
                melhorSobra = sobra;
            }
        }

        return pecas
            .Select((p, i) => p with { Area = melhor![i] })
            .OrderBy(p => Math.Round(p.Area.Y, 3)).ThenBy(p => p.Area.X)
            .ToList();
    }

    /// <summary>Área do maior retângulo livre que sobra na chapa (onde ainda caberia outra peça).</summary>
    private static double MaiorSobra(Retangulo[] pecas, Retangulo limites, List<Retangulo> fora, double k, TipoCorte tipo) =>
        RetanguloDaMaiorSobra(pecas, limites, fora, k, tipo)?.Area ?? 0;

    /// <summary>
    /// O maior retângulo livre da chapa, longe das peças o bastante para o corte: na serra, a espessura em volta
    /// toda; na CNC, só dos lados (pela ponta pode encostar).
    /// </summary>
    private static Retangulo? RetanguloDaMaiorSobra(Retangulo[] pecas, Retangulo limites, List<Retangulo> fora, double k,
        TipoCorte tipo)
    {
        var ocupados = tipo == TipoCorte.Cnc
            ? pecas.SelectMany(p => OcupadoNaCnc(p, k))
            : pecas.Select(p => p.Expandido(k));
        var livres = new EncaixeLivre(limites, fora.Concat(ocupados));
        return livres.Livres.Count == 0 ? null : livres.Livres.MaxBy(r => r.Area);
    }

    /// <summary>
    /// Empurra cada peça para baixo e depois para a esquerda até encostar numa borda, num furo ou em outra
    /// peça (de frente: com a fresa entre elas; na diagonal: encostando pela ponta), repetindo até nada mais se mexer.
    /// </summary>
    private static Retangulo[] EmpurrarParaOCanto(Retangulo[] areas, Retangulo limites, List<Retangulo> fora, double k)
    {
        for (var volta = 0; volta < 50; volta++)
        {
            var mexeu = false;
            foreach (var i in Enumerable.Range(0, areas.Length).OrderBy(i => areas[i].Y).ThenBy(i => areas[i].X))
            {
                var r = areas[i];

                var y = limites.Y;
                for (var j = 0; j < areas.Length; j++)
                {
                    var q = areas[j];
                    if (j == i || q.Topo > r.Y + Eps)
                        continue;
                    if (q.X < r.Direita - Eps && q.Direita > r.X + Eps)
                        y = Math.Max(y, q.Topo + k);   // de frente: a fresa passa entre elas
                    else if (q.X < r.Direita + k - Eps && q.Direita + k > r.X + Eps)
                        y = Math.Max(y, q.Topo);       // na diagonal, perto: para quando encostar pela ponta
                }
                foreach (var o in fora)
                {
                    if (o.Topo <= r.Y + Eps && o.X < r.Direita - Eps && o.Direita > r.X + Eps)
                        y = Math.Max(y, o.Topo);
                }
                if (y < r.Y - 0.01)
                {
                    r = r with { Y = y };
                    mexeu = true;
                }

                var x = limites.X;
                for (var j = 0; j < areas.Length; j++)
                {
                    var q = areas[j];
                    if (j == i || q.Direita > r.X + Eps)
                        continue;
                    if (q.Y < r.Topo - Eps && q.Topo > r.Y + Eps)
                        x = Math.Max(x, q.Direita + k);
                    else if (q.Y < r.Topo + k - Eps && q.Topo + k > r.Y + Eps)
                        x = Math.Max(x, q.Direita);
                }
                foreach (var o in fora)
                {
                    if (o.Direita <= r.X + Eps && o.Y < r.Topo - Eps && o.Topo > r.Y + Eps)
                        x = Math.Max(x, o.Direita);
                }
                if (x < r.X - 0.01)
                {
                    r = r with { X = x };
                    mexeu = true;
                }

                areas[i] = r;
            }
            if (!mexeu)
                break;
        }
        return areas;
    }

    // ------------------------------------------------------------------ Serra (guilhotina)

    private static Enchimento EncherChapaSerra(List<Item> restantes, Retangulo limites, List<Retangulo> fora,
        List<CorteSerra> preparo, double k, RegraDeEncaixe regra, RegraDeDivisao divisao, Func<Retangulo, bool>? soAr)
    {
        var encaixe = new EncaixeGuilhotina(limites, fora, k);
        var colocadas = new List<(Item, Retangulo, bool)>();
        foreach (var item in restantes)
        {
            if (encaixe.Procurar(item.W, item.H, regra) is not { } lugar)
                continue;
            var area = encaixe.Colocar(lugar.Pedaco, lugar.Lugar, divisao, colocadas.Count);
            colocadas.Add((item, area, lugar.Girada));
        }
        if (colocadas.Count == 0)
            return new Enchimento(colocadas, []);

        var cortes = new List<CorteSerra>(preparo);
        AdicionarCortes(encaixe.Raiz, cortes, k, soAr);
        return new Enchimento(colocadas, cortes);
    }

    /// <summary>
    /// Serra, por tipo de peça: todas as cópias de um tipo de uma vez, no espaço onde cabem mais delas,
    /// arrumadas do melhor jeito (girando algumas quando isso aproveita a sobra), com os cortes do bloco.
    /// </summary>
    private static Enchimento EncherChapaSerraEmBlocos(List<Item> restantes, Retangulo limites, List<Retangulo> fora,
        List<CorteSerra> preparo, double k, RegraDeDivisao divisao, Func<Retangulo, bool>? soAr,
        Dictionary<int, PadraoDePecasIguais> padroes)
    {
        var encaixe = new EncaixeGuilhotina(limites, fora, k);
        var colocadas = new List<(Item, Retangulo, bool)>();
        foreach (var grupo in restantes.GroupBy(i => i.Peca.Numero))
        {
            var fila = new Queue<Item>(grupo);
            var padrao = padroes[grupo.Key];
            while (fila.Count > 0 && encaixe.ProcurarBloco(padrao, fila.Count) is { } bloco)
            {
                foreach (var (lugar, girada) in encaixe.ColocarBloco(bloco.Pedaco, bloco.Regiao, padrao, bloco.Quantidade,
                             divisao, colocadas.Count))
                    colocadas.Add((fila.Dequeue(), lugar, girada));
            }
        }
        if (colocadas.Count == 0)
            return new Enchimento(colocadas, []);

        var cortes = new List<CorteSerra>(preparo);
        AdicionarCortes(encaixe.Raiz, cortes, k, soAr);
        return new Enchimento(colocadas, cortes);
    }

    /// <summary>Percorre a árvore de cortes: o corte de um pedaço vem antes dos cortes das partes dele.</summary>
    private static void AdicionarCortes(PedacoDeCorte pedaco, List<CorteSerra> cortes, double k, Func<Retangulo, bool>? soAr)
    {
        if (pedaco.Direcao is not { } direcao)
            return;
        // Pedaço que só ficou com sobra não precisa ser cortado
        if (!TemPeca(pedaco))
            return;

        var a = pedaco.Area;
        if (Necessario(pedaco, direcao, k, soAr))
        {
            var (inicio, fim) = direcao == DirecaoCorte.Vertical ? (a.Y, a.Topo) : (a.X, a.Direita);
            var texto = direcao == DirecaoCorte.Vertical
                ? $"No pedaço de {a.Comprimento:0.#} × {a.Largura:0.#} mm, cortar na vertical a {pedaco.Distancia:0.#} mm da borda esquerda."
                : $"No pedaço de {a.Comprimento:0.#} × {a.Largura:0.#} mm, cortar na horizontal a {pedaco.Distancia:0.#} mm da borda de baixo.";
            cortes.Add(new CorteSerra(cortes.Count + 1, TipoDePasso.Corte, direcao, pedaco.Posicao, inicio, fim, a,
                pedaco.Distancia, texto));
        }

        if (pedaco.Primeiro is not null)
            AdicionarCortes(pedaco.Primeiro, cortes, k, soAr);
        if (pedaco.Segundo is not null)
            AdicionarCortes(pedaco.Segundo, cortes, k, soAr);
    }

    /// <summary>
    /// Na chapa irregular, se de um lado do corte (contando a faixa da serra) só tem ar, a chapa já termina
    /// ali: o corte não precisa ser feito.
    /// </summary>
    private static bool Necessario(PedacoDeCorte pedaco, DirecaoCorte direcao, double k, Func<Retangulo, bool>? soAr)
    {
        if (soAr is null || pedaco.Obstaculos.Count == 0)
            return true;
        var a = pedaco.Area;
        var p = pedaco.Posicao;
        var (antes, depois) = direcao == DirecaoCorte.Vertical
            ? (new Retangulo(a.X, a.Y, Math.Min(p + k, a.Direita) - a.X, a.Largura),
               new Retangulo(p, a.Y, a.Direita - p, a.Largura))
            : (new Retangulo(a.X, a.Y, a.Comprimento, Math.Min(p + k, a.Topo) - a.Y),
               new Retangulo(a.X, p, a.Comprimento, a.Topo - p));
        var arAntes = CobertoPorObstaculos(antes, pedaco.Obstaculos) && soAr(antes);
        var arDepois = CobertoPorObstaculos(depois, pedaco.Obstaculos) && soAr(depois);
        return !arAntes && !arDepois;
    }

    /// <summary>Se o retângulo cai todo em partes que não servem (só aí vale a pena perguntar se é ar).</summary>
    private static bool CobertoPorObstaculos(Retangulo r, IReadOnlyList<Retangulo> obstaculos)
    {
        var coberto = 0.0;
        foreach (var o in obstaculos)
        {
            var w = Math.Min(o.Direita, r.Direita) - Math.Max(o.X, r.X);
            var h = Math.Min(o.Topo, r.Topo) - Math.Max(o.Y, r.Y);
            if (w > 0 && h > 0)
                coberto += w * h;
        }
        return coberto >= r.Area - 1;
    }

    private static bool TemPeca(PedacoDeCorte p) =>
        p.Peca >= 0 || (p.Primeiro is not null && TemPeca(p.Primeiro)) || (p.Segundo is not null && TemPeca(p.Segundo));

    /// <summary>Cortes antes das peças: aparar as quatro bordas (quando há o que aparar).</summary>
    private static List<CorteSerra> CortesDePreparo(FormatoChapa chapa, Retangulo util, double refilo)
    {
        var cortes = new List<CorteSerra>();
        if (refilo <= 0)
            return cortes;

        var s = chapa.Limites;
        var u = util;
        Adicionar(DirecaoCorte.Horizontal, u.Y, s.X, s.Direita, "de baixo", u.Y - s.Y);
        Adicionar(DirecaoCorte.Vertical, u.X, s.Y, s.Topo, "da esquerda", u.X - s.X);
        Adicionar(DirecaoCorte.Horizontal, u.Topo, s.X, s.Direita, "de cima", s.Topo - u.Topo);
        Adicionar(DirecaoCorte.Vertical, u.Direita, s.Y, s.Topo, "da direita", s.Direita - u.Direita);
        return cortes;

        void Adicionar(DirecaoCorte direcao, double posicao, double inicio, double fim, string borda, double distancia)
        {
            // Chapa irregular: a borda mais de fora pode ficar mais longe que a medida a aparar
            var texto = Math.Abs(distancia - refilo) < 0.05
                ? $"Aparar a borda {borda}: tirar {refilo:0.#} mm."
                : $"Aparar a borda {borda}: cortar a {distancia:0.#} mm da ponta da chapa.";
            cortes.Add(new CorteSerra(cortes.Count + 1, TipoDePasso.Refilo, direcao, posicao, inicio, fim, s, distancia, texto));
        }
    }

    private static ResultadoPlano Montar(TipoCorte tipo, FormatoChapa chapa, double refilo, double espessura, Tentativa t)
    {
        var chapas = t.Chapas.Select(c => c with { AreaChapa = chapa.Area }).ToList();
        var naoCouberam = t.Sobraram
            .GroupBy(i => i.Peca)
            .Select(g => (g.Key, g.Count()))
            .ToList();
        return new ResultadoPlano(tipo, chapa, refilo, espessura, chapas, naoCouberam);
    }
}
