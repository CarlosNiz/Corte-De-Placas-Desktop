using System;
using System.Collections.Generic;
using System.Linq;

namespace CorteDePlacasDesktop.Models;

/// <summary>
/// Calcula o plano de corte: em quantas chapas as peças cabem e onde fica cada uma.
/// Testa várias estratégias (ordem das peças, como escolher o espaço, como dividir os pedaços)
/// e fica com a melhor: menos peças de fora, depois menos chapas, depois a última chapa mais vazia
/// (as sobras juntas no fim) e, na serra, menos cortes.
/// </summary>
public static class CalculadoraDePlano
{
    private const double Eps = 1e-6;

    /// <summary>Uma unidade de peça a colocar.</summary>
    private sealed record Item(PecaPedida Peca, int Copia)
    {
        public double W => Peca.Comprimento;
        public double H => Peca.Largura;
    }

    private sealed record Tentativa(List<ChapaDoPlano> Chapas, List<Item> Sobraram);

    private static readonly Func<Item, double>[] Ordens =
    [
        i => i.W * i.H,
        i => Math.Max(i.W, i.H),
        i => Math.Min(i.W, i.H),
        i => i.W + i.H,
        i => i.W,
        i => i.H,
    ];

    public static ResultadoPlano Calcular(FormatoChapa chapa, double refilo, double espessura, TipoCorte tipo,
        IReadOnlyList<PecaPedida> pecas)
    {
        var util = chapa.AreaUtil(refilo);
        var itens = pecas.SelectMany(p => Enumerable.Range(1, p.Quantidade).Select(c => new Item(p, c))).ToList();

        if (util.Count == 0 || itens.Count == 0)
            return Montar(tipo, chapa, refilo, espessura, new Tentativa([], itens));

        var limites = Geometria.Limites(util.SelectMany(p => p));
        var fora = Geometria.RetangulosFora(util, limites);
        var areaChapa = chapa.Area;

        // Na serra, uma chapa irregular primeiro vira blocos retangulares (iguais em todas as chapas).
        var blocos = tipo == TipoCorte.Serra
            ? DividirEmBlocos(limites, fora, espessura, itens)
            : [];
        var preparo = tipo == TipoCorte.Serra
            ? CortesDePreparo(chapa, util, refilo, espessura, blocos, fora.Count == 0)
            : [];

        Tentativa? melhor = null;
        foreach (var ordem in Ordens)
        {
            var ordenados = itens.OrderByDescending(ordem).ThenByDescending(i => i.W * i.H).ToList();
            foreach (var regra in Enum.GetValues<RegraDeEncaixe>())
            {
                if (tipo == TipoCorte.Cnc)
                {
                    melhor = Melhor(melhor, Simular(ordenados, itensRestantes =>
                        EncherChapaLivre(itensRestantes, limites, fora, espessura, regra, areaChapa)));
                }
                else
                {
                    foreach (var divisao in Enum.GetValues<RegraDeDivisao>())
                    {
                        melhor = Melhor(melhor, Simular(ordenados, itensRestantes =>
                            EncherChapaSerra(itensRestantes, blocos, preparo, espessura, regra, divisao, areaChapa)));
                    }
                }
            }
        }

        return Montar(tipo, chapa, refilo, espessura, melhor!);
    }

    /// <summary>Enche chapas, uma de cada vez, até acabarem as peças (ou nenhuma caber numa chapa vazia).</summary>
    private static Tentativa Simular(List<Item> ordenados,
        Func<List<Item>, (List<(Item Item, Retangulo Area, bool Girada)> Colocadas, List<CorteSerra> Cortes)> encher)
    {
        var restantes = new List<Item>(ordenados);
        var chapas = new List<ChapaDoPlano>();
        while (restantes.Count > 0)
        {
            var (colocadas, cortes) = encher(restantes);
            if (colocadas.Count == 0)
                break;
            foreach (var c in colocadas)
                restantes.Remove(c.Item);

            var pecas = colocadas
                .OrderBy(c => Math.Round(c.Area.Y, 3)).ThenBy(c => c.Area.X)
                .Select(c => new PecaNaChapa(c.Item.Peca.Numero, c.Item.Copia, c.Item.Peca.Descricao, c.Area, c.Girada,
                    c.Item.Peca.Formato, c.Item.Peca.AreaUnitaria))
                .ToList();
            chapas.Add(new ChapaDoPlano(chapas.Count + 1, pecas, cortes, 0));
        }
        return new Tentativa(chapas, restantes);
    }

    private static Tentativa Melhor(Tentativa? atual, Tentativa nova)
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

        var cortesNova = nova.Chapas.Sum(c => c.Cortes.Count);
        var cortesAtual = atual.Chapas.Sum(c => c.Cortes.Count);
        return cortesNova < cortesAtual ? nova : atual;
    }

    // ------------------------------------------------------------------ CNC (encaixe livre)

    private static (List<(Item, Retangulo, bool)>, List<CorteSerra>) EncherChapaLivre(List<Item> restantes,
        Retangulo limites, List<Retangulo> fora, double k, RegraDeEncaixe regra, double areaChapa)
    {
        // Truque da espessura: cada peça vira (w + k) × (h + k) e a área cresce k à direita e em cima.
        // As partes de fora encolhem k à esquerda e embaixo; assim a conta continua exata.
        // Partes de fora mais finas que k não podem sumir: ficam com uma lasca mínima (conta um pouco
        // mais cautelosa ali, no máximo k).
        var area = new Retangulo(limites.X, limites.Y, limites.Comprimento + k, limites.Largura + k);
        var ocupados = fora.Select(o => new Retangulo(
            o.X + k, o.Y + k,
            Math.Max(o.Comprimento - k, 0.01),
            Math.Max(o.Largura - k, 0.01)));
        var encaixe = new EncaixeLivre(area, ocupados);

        var colocadas = new List<(Item, Retangulo, bool)>();
        foreach (var item in restantes)
        {
            if (encaixe.Procurar(item.W + k, item.H + k, regra) is not { } lugar)
                continue;
            encaixe.Ocupar(lugar.Posicao);
            var (w, h) = lugar.Girada ? (item.H, item.W) : (item.W, item.H);
            colocadas.Add((item, new Retangulo(lugar.Posicao.X, lugar.Posicao.Y, w, h), lugar.Girada));
        }
        return (colocadas, []);
    }

    // ------------------------------------------------------------------ Serra (guilhotina)

    private static (List<(Item, Retangulo, bool)>, List<CorteSerra>) EncherChapaSerra(List<Item> restantes,
        List<Retangulo> blocos, List<CorteSerra> preparo, double k, RegraDeEncaixe regra, RegraDeDivisao divisao,
        double areaChapa)
    {
        var encaixe = new EncaixeGuilhotina(blocos, k);
        var colocadas = new List<(Item, Retangulo, bool)>();
        foreach (var item in restantes)
        {
            if (encaixe.Procurar(item.W, item.H, regra) is not { } lugar)
                continue;
            var (w, h) = lugar.Girada ? (item.H, item.W) : (item.W, item.H);
            var area = encaixe.Colocar(lugar.Pedaco, w, h, divisao, colocadas.Count);
            colocadas.Add((item, area, lugar.Girada));
        }
        if (colocadas.Count == 0)
            return (colocadas, []);

        var cortes = new List<CorteSerra>(preparo);
        foreach (var raiz in encaixe.Raizes)
            AdicionarCortes(raiz, cortes);
        return (colocadas, cortes);
    }

    /// <summary>Percorre a árvore de cortes: o corte de um pedaço vem antes dos cortes das partes dele.</summary>
    private static void AdicionarCortes(PedacoDeCorte pedaco, List<CorteSerra> cortes)
    {
        if (pedaco.Direcao is not { } direcao)
            return;
        // Pedaço que só ficou com sobra não precisa ser cortado
        if (!TemPeca(pedaco))
            return;

        var a = pedaco.Area;
        var (inicio, fim) = direcao == DirecaoCorte.Vertical ? (a.Y, a.Topo) : (a.X, a.Direita);
        var texto = direcao == DirecaoCorte.Vertical
            ? $"No pedaço de {a.Comprimento:0.#} × {a.Largura:0.#} mm, cortar na vertical a {pedaco.Distancia:0.#} mm da borda esquerda."
            : $"No pedaço de {a.Comprimento:0.#} × {a.Largura:0.#} mm, cortar na horizontal a {pedaco.Distancia:0.#} mm da borda de baixo.";
        cortes.Add(new CorteSerra(cortes.Count + 1, TipoDePasso.Corte, direcao, pedaco.Posicao, inicio, fim, a,
            pedaco.Distancia, texto));

        if (pedaco.Primeiro is not null)
            AdicionarCortes(pedaco.Primeiro, cortes);
        if (pedaco.Segundo is not null)
            AdicionarCortes(pedaco.Segundo, cortes);
    }

    private static bool TemPeca(PedacoDeCorte p) =>
        p.Peca >= 0 || (p.Primeiro is not null && TemPeca(p.Primeiro)) || (p.Segundo is not null && TemPeca(p.Segundo));

    /// <summary>
    /// Chapa retangular: um bloco só (a área útil). Chapa irregular: os maiores retângulos que cabem nela,
    /// um de cada vez, separados pela espessura do corte, enquanto ainda couber alguma peça.
    /// </summary>
    private static List<Retangulo> DividirEmBlocos(Retangulo limites, List<Retangulo> fora, double k, List<Item> itens)
    {
        if (fora.Count == 0)
            return [limites];

        var menorLado = itens.Min(i => Math.Min(i.W, i.H));
        var menorArea = itens.Min(i => i.W * i.H);
        var livres = new EncaixeLivre(limites, fora);
        var blocos = new List<Retangulo>();
        while (blocos.Count < 30)
        {
            var maior = livres.Livres
                .Where(r => Math.Min(r.Comprimento, r.Largura) >= menorLado - Eps && r.Area >= menorArea - Eps)
                .OrderByDescending(r => r.Area)
                .FirstOrDefault();
            if (maior.Area <= 0)
                break;
            blocos.Add(maior);
            livres.Ocupar(maior.Expandido(k));
        }
        return blocos;
    }

    /// <summary>Cortes antes das peças: refilar as bordas (chapa retangular) ou separar os blocos (chapa irregular).</summary>
    private static List<CorteSerra> CortesDePreparo(FormatoChapa chapa, IReadOnlyList<IReadOnlyList<Ponto>> util,
        double refilo, double k, List<Retangulo> blocos, bool retangular)
    {
        var cortes = new List<CorteSerra>();
        if (retangular && blocos.Count == 1)
        {
            if (refilo <= 0)
                return cortes;
            var s = chapa.Limites;
            var u = blocos[0];
            Adicionar(TipoDePasso.Refilo, DirecaoCorte.Horizontal, u.Y, s.X, s.Direita, s, refilo, $"Aparar a borda de baixo: tirar {refilo:0.#} mm.");
            Adicionar(TipoDePasso.Refilo, DirecaoCorte.Vertical, u.X, s.Y, s.Topo, s, refilo, $"Aparar a borda da esquerda: tirar {refilo:0.#} mm.");
            Adicionar(TipoDePasso.Refilo, DirecaoCorte.Horizontal, u.Topo, s.X, s.Direita, s, refilo, $"Aparar a borda de cima: tirar {refilo:0.#} mm.");
            Adicionar(TipoDePasso.Refilo, DirecaoCorte.Vertical, u.Direita, s.Y, s.Topo, s, refilo, $"Aparar a borda da direita: tirar {refilo:0.#} mm.");
            return cortes;
        }

        // Chapa irregular: cada lado de cada bloco é um corte (menos os que já são a borda da chapa sem refilo
        // e os que coincidem com um corte anterior).
        for (var b = 0; b < blocos.Count; b++)
        {
            var bloco = blocos[b];
            var lados = new (DirecaoCorte Direcao, double Posicao, double Inicio, double Fim, Ponto Fora)[]
            {
                (DirecaoCorte.Horizontal, bloco.Y, bloco.X, bloco.Direita, new Ponto(bloco.Centro.X, bloco.Y - 1)),
                (DirecaoCorte.Vertical, bloco.X, bloco.Y, bloco.Topo, new Ponto(bloco.X - 1, bloco.Centro.Y)),
                (DirecaoCorte.Horizontal, bloco.Topo, bloco.X, bloco.Direita, new Ponto(bloco.Centro.X, bloco.Topo + 1)),
                (DirecaoCorte.Vertical, bloco.Direita, bloco.Y, bloco.Topo, new Ponto(bloco.Direita + 1, bloco.Centro.Y)),
            };
            foreach (var lado in lados)
            {
                if (refilo <= 0 && !Geometria.PontoDentroRegiao(chapa.Regiao, lado.Fora))
                    continue;
                var repetido = cortes.Any(c => c.Direcao == lado.Direcao && Math.Abs(c.Posicao - lado.Posicao) <= k + Eps
                                               && c.Inicio <= lado.Inicio + Eps && c.Fim >= lado.Fim - Eps);
                if (repetido)
                    continue;
                var direcaoTexto = lado.Direcao == DirecaoCorte.Vertical ? $"vertical em X = {lado.Posicao:0.#} mm" : $"horizontal em Y = {lado.Posicao:0.#} mm";
                Adicionar(TipoDePasso.SepararBloco, lado.Direcao, lado.Posicao, lado.Inicio, lado.Fim, bloco, 0,
                    $"Separar o bloco {b + 1} ({bloco.Comprimento:0.#} × {bloco.Largura:0.#} mm): corte {direcaoTexto}.");
            }
        }
        return cortes;

        void Adicionar(TipoDePasso tipo, DirecaoCorte direcao, double posicao, double inicio, double fim, Retangulo pedaco,
            double distancia, string texto) =>
            cortes.Add(new CorteSerra(cortes.Count + 1, tipo, direcao, posicao, inicio, fim, pedaco, distancia, texto));
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
