using System;
using System.Collections.Generic;
using System.Linq;

namespace CorteDePlacasDesktop.Models;

/// <summary>Ao colocar uma peça num pedaço, qual corte fazer primeiro.</summary>
internal enum RegraDeDivisao
{
    /// <summary>Sempre corta primeiro na horizontal (faixas deitadas).</summary>
    FaixasHorizontais,

    /// <summary>Sempre corta primeiro na vertical (faixas em pé).</summary>
    FaixasVerticais,

    /// <summary>Escolhe o corte que deixa a maior sobra inteira.</summary>
    MaiorSobra,

    /// <summary>Corta primeiro no eixo onde sobra menos (deixa a sobra grande do outro lado).</summary>
    EixoDeMenorSobra,
}

/// <summary>
/// Um pedaço da chapa na árvore de cortes da serra. Ou está livre, ou é uma peça,
/// ou foi dividido por um corte em <see cref="Primeiro"/> (onde fica a peça) e <see cref="Segundo"/> (o resto).
/// </summary>
internal sealed class PedacoDeCorte(Retangulo area)
{
    public Retangulo Area { get; } = area;

    public DirecaoCorte? Direcao { get; set; }

    /// <summary>Onde a serra entra: X (vertical) ou Y (horizontal) do fim da primeira parte.</summary>
    public double Posicao { get; set; }

    /// <summary>Tamanho da primeira parte (distância da borda do pedaço até o corte).</summary>
    public double Distancia { get; set; }

    public PedacoDeCorte? Primeiro { get; set; }

    public PedacoDeCorte? Segundo { get; set; }

    /// <summary>Índice da peça que ocupa este pedaço (ou -1).</summary>
    public int Peca { get; set; } = -1;
}

/// <summary>
/// Encaixe para serra (guilhotina): cada peça é tirada de um pedaço livre com cortes que
/// atravessam o pedaço inteiro. Guarda a árvore de cortes para montar a ordem dos cortes.
/// </summary>
internal sealed class EncaixeGuilhotina
{
    private const double Eps = 1e-6;
    private readonly double _espessura;
    private readonly List<PedacoDeCorte> _livres = new();

    /// <param name="blocos">Retângulos de onde as peças saem (a chapa inteira, ou blocos de uma chapa irregular).</param>
    /// <param name="espessura">Material perdido em cada corte.</param>
    public EncaixeGuilhotina(IEnumerable<Retangulo> blocos, double espessura)
    {
        _espessura = espessura;
        Raizes = blocos.Select(b => new PedacoDeCorte(b)).ToList();
        _livres.AddRange(Raizes);
    }

    public IReadOnlyList<PedacoDeCorte> Raizes { get; }

    public (PedacoDeCorte Pedaco, bool Girada, double Nota1, double Nota2)? Procurar(double comprimento, double largura, RegraDeEncaixe regra)
    {
        (PedacoDeCorte, bool, double, double)? melhor = null;
        foreach (var livre in _livres)
        {
            Testar(livre, comprimento, largura, false);
            if (Math.Abs(comprimento - largura) > Eps)
                Testar(livre, largura, comprimento, true);
        }
        return melhor;

        void Testar(PedacoDeCorte pedaco, double w, double h, bool girada)
        {
            var a = pedaco.Area;
            if (w > a.Comprimento + Eps || h > a.Largura + Eps)
                return;
            var sobraX = a.Comprimento - w;
            var sobraY = a.Largura - h;
            var (n1, n2) = regra switch
            {
                RegraDeEncaixe.MenorSobraCurta => (Math.Min(sobraX, sobraY), Math.Max(sobraX, sobraY)),
                RegraDeEncaixe.MenorSobraLonga => (Math.Max(sobraX, sobraY), Math.Min(sobraX, sobraY)),
                RegraDeEncaixe.MenorArea => (a.Area - w * h, Math.Min(sobraX, sobraY)),
                _ => (a.Y + h, a.X),
            };
            if (melhor is null || n1 < melhor.Value.Item3 - Eps
                || (Math.Abs(n1 - melhor.Value.Item3) <= Eps && n2 < melhor.Value.Item4))
            {
                melhor = (pedaco, girada, n1, n2);
            }
        }
    }

    /// <summary>
    /// Tira uma peça w × h do canto inferior esquerdo do pedaço, com até dois cortes,
    /// e devolve onde ela ficou.
    /// </summary>
    public Retangulo Colocar(PedacoDeCorte pedaco, double w, double h, RegraDeDivisao divisao, int peca)
    {
        _livres.Remove(pedaco);
        var a = pedaco.Area;
        var k = _espessura;
        var precisaVertical = w < a.Comprimento - Eps;
        var precisaHorizontal = h < a.Largura - Eps;

        var horizontalPrimeiro = divisao switch
        {
            RegraDeDivisao.FaixasHorizontais => true,
            RegraDeDivisao.FaixasVerticais => false,
            RegraDeDivisao.MaiorSobra =>
                Math.Max(Area(a.Comprimento - w - k, h), Area(a.Comprimento, a.Largura - h - k))
                >= Math.Max(Area(a.Comprimento - w - k, a.Largura), Area(w, a.Largura - h - k)),
            _ => a.Comprimento - w <= a.Largura - h,
        };

        PedacoDeCorte final;
        if (horizontalPrimeiro)
        {
            var faixa = precisaHorizontal ? Cortar(pedaco, DirecaoCorte.Horizontal, h) : pedaco;
            final = precisaVertical ? Cortar(faixa, DirecaoCorte.Vertical, w) : faixa;
        }
        else
        {
            var faixa = precisaVertical ? Cortar(pedaco, DirecaoCorte.Vertical, w) : pedaco;
            final = precisaHorizontal ? Cortar(faixa, DirecaoCorte.Horizontal, h) : faixa;
        }

        final.Peca = peca;
        return final.Area;
    }

    /// <summary>Corta o pedaço a <paramref name="distancia"/> da borda esquerda/de baixo; devolve a primeira parte.</summary>
    private PedacoDeCorte Cortar(PedacoDeCorte pedaco, DirecaoCorte direcao, double distancia)
    {
        var a = pedaco.Area;
        var k = _espessura;
        pedaco.Direcao = direcao;
        pedaco.Distancia = distancia;

        if (direcao == DirecaoCorte.Vertical)
        {
            pedaco.Posicao = a.X + distancia;
            pedaco.Primeiro = new PedacoDeCorte(new Retangulo(a.X, a.Y, distancia, a.Largura));
            var resto = a.Comprimento - distancia - k;
            if (resto > Eps)
                pedaco.Segundo = new PedacoDeCorte(new Retangulo(a.X + distancia + k, a.Y, resto, a.Largura));
        }
        else
        {
            pedaco.Posicao = a.Y + distancia;
            pedaco.Primeiro = new PedacoDeCorte(new Retangulo(a.X, a.Y, a.Comprimento, distancia));
            var resto = a.Largura - distancia - k;
            if (resto > Eps)
                pedaco.Segundo = new PedacoDeCorte(new Retangulo(a.X, a.Y + distancia + k, a.Comprimento, resto));
        }

        if (pedaco.Segundo is not null)
            _livres.Add(pedaco.Segundo);
        return pedaco.Primeiro;
    }

    private static double Area(double w, double h) => w > 0 && h > 0 ? w * h : 0;
}
