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
/// ou foi dividido por um corte em <see cref="Primeiro"/> (esquerda/baixo) e <see cref="Segundo"/> (direita/cima).
/// </summary>
/// <param name="obstaculos">Partes do pedaço que não podem virar peça (fora da chapa, furos, bordas aparadas).</param>
internal sealed class PedacoDeCorte(Retangulo area, IReadOnlyList<Retangulo> obstaculos)
{
    private IReadOnlyList<Retangulo>? _espacos;

    public Retangulo Area { get; } = area;

    public IReadOnlyList<Retangulo> Obstaculos { get; } = obstaculos;

    /// <summary>Maiores retângulos vazios do pedaço (o pedaço inteiro quando não há obstáculos).</summary>
    public IReadOnlyList<Retangulo> Espacos => _espacos ??= Obstaculos.Count == 0
        ? [Area]
        : new EncaixeLivre(Area, Obstaculos).Livres.ToList();

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
/// atravessam o pedaço inteiro. Funciona também com chapa irregular: as partes que não servem
/// (furos, recortes, bordas) ficam como obstáculos dentro dos pedaços, e a peça vai num canto
/// de um espaço vazio. Guarda a árvore de cortes para montar a ordem dos cortes.
/// </summary>
internal sealed class EncaixeGuilhotina
{
    private const double Eps = 1e-6;
    private readonly double _espessura;
    private readonly List<PedacoDeCorte> _livres = new();

    /// <param name="area">Retângulo de onde as peças saem (a área útil da chapa).</param>
    /// <param name="obstaculos">Partes da área que não podem virar peça.</param>
    /// <param name="espessura">Material perdido em cada corte.</param>
    public EncaixeGuilhotina(Retangulo area, IReadOnlyList<Retangulo> obstaculos, double espessura)
    {
        _espessura = espessura;
        Raiz = new PedacoDeCorte(area, Recortar(obstaculos, area));
        _livres.Add(Raiz);
    }

    public PedacoDeCorte Raiz { get; }

    /// <summary>
    /// Procura onde colocar uma peça comprimento × largura (pode girar): o pedaço, o lugar exato e se girou.
    /// </summary>
    public (PedacoDeCorte Pedaco, Retangulo Lugar, bool Girada)? Procurar(double comprimento, double largura, RegraDeEncaixe regra)
    {
        PedacoDeCorte? melhorPedaco = null;
        var melhorLugar = default(Retangulo);
        var melhorGirada = false;
        var melhorNota = (N1: double.MaxValue, N2: double.MaxValue, Cortes: int.MaxValue, Y: double.MaxValue, X: double.MaxValue);

        foreach (var pedaco in _livres)
        {
            var a = pedaco.Area;
            if (Math.Min(comprimento, largura) > Math.Min(a.Comprimento, a.Largura) + Eps
                || Math.Max(comprimento, largura) > Math.Max(a.Comprimento, a.Largura) + Eps)
                continue;
            foreach (var espaco in pedaco.Espacos)
            {
                Testar(pedaco, espaco, comprimento, largura, false);
                if (Math.Abs(comprimento - largura) > Eps)
                    Testar(pedaco, espaco, largura, comprimento, true);
            }
        }
        return melhorPedaco is null ? null : (melhorPedaco, melhorLugar, melhorGirada);

        void Testar(PedacoDeCorte pedaco, Retangulo espaco, double w, double h, bool girada)
        {
            if (w > espaco.Comprimento + Eps || h > espaco.Largura + Eps)
                return;
            var sobraX = espaco.Comprimento - w;
            var sobraY = espaco.Largura - h;

            // Nos 4 cantos do espaço vazio; o que precisar de menos cortes ganha.
            foreach (var x in new[] { espaco.X, espaco.Direita - w })
            {
                foreach (var y in new[] { espaco.Y, espaco.Topo - h })
                {
                    var lugar = Ajustar(pedaco.Area, espaco, x, y, w, h);
                    if (lugar is not { } l)
                        continue;
                    var (n1, n2) = regra switch
                    {
                        RegraDeEncaixe.MenorSobraCurta => (Math.Min(sobraX, sobraY), Math.Max(sobraX, sobraY)),
                        RegraDeEncaixe.MenorSobraLonga => (Math.Max(sobraX, sobraY), Math.Min(sobraX, sobraY)),
                        RegraDeEncaixe.MenorArea => (espaco.Area - w * h, Math.Min(sobraX, sobraY)),
                        _ => (l.Topo, l.X),
                    };
                    var nota = (n1, n2, Cortes(pedaco.Area, l), l.Y, l.X);
                    if (melhorPedaco is null || Menor(nota, melhorNota))
                    {
                        melhorPedaco = pedaco;
                        melhorLugar = l;
                        melhorGirada = girada;
                        melhorNota = nota;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Uma peça longe da borda esquerda/de baixo do pedaço precisa de espaço para o corte que a separa
    /// do que fica antes dela. Se não der dentro do espaço vazio, esse canto não serve.
    /// </summary>
    private Retangulo? Ajustar(Retangulo pedaco, Retangulo espaco, double x, double y, double w, double h)
    {
        var k = _espessura;
        if (x > pedaco.X + Eps && x < pedaco.X + k)
            x = pedaco.X + k;
        if (y > pedaco.Y + Eps && y < pedaco.Y + k)
            y = pedaco.Y + k;
        if (x + w > espaco.Direita + Eps || y + h > espaco.Topo + Eps)
            return null;
        return new Retangulo(x, y, w, h);
    }

    private static int Cortes(Retangulo pedaco, Retangulo lugar) =>
        (lugar.X > pedaco.X + Eps ? 1 : 0) + (lugar.Y > pedaco.Y + Eps ? 1 : 0)
        + (lugar.Direita < pedaco.Direita - Eps ? 1 : 0) + (lugar.Topo < pedaco.Topo - Eps ? 1 : 0);

    private static bool Menor((double N1, double N2, int Cortes, double Y, double X) a,
        (double N1, double N2, int Cortes, double Y, double X) b)
    {
        if (Math.Abs(a.N1 - b.N1) > Eps)
            return a.N1 < b.N1;
        if (Math.Abs(a.N2 - b.N2) > Eps)
            return a.N2 < b.N2;
        if (a.Cortes != b.Cortes)
            return a.Cortes < b.Cortes;
        if (Math.Abs(a.Y - b.Y) > Eps)
            return a.Y < b.Y;
        return a.X < b.X - Eps;
    }

    /// <summary>
    /// Tira a peça do lugar escolhido no pedaço com até quatro cortes (antes e depois dela, nos dois sentidos)
    /// e devolve onde ela ficou.
    /// </summary>
    public Retangulo Colocar(PedacoDeCorte pedaco, Retangulo lugar, RegraDeDivisao divisao, int peca)
    {
        _livres.Remove(pedaco);
        var final = Isolar(pedaco, lugar, divisao);
        final.Peca = peca;
        return final.Area;
    }

    /// <summary>
    /// Procura o espaço livre onde cabe o maior bloco de peças iguais (até <paramref name="quantidade"/>),
    /// arrumadas do melhor jeito (deitadas e em pé). Devolve o pedaço, o retângulo do bloco e quantas peças.
    /// </summary>
    public (PedacoDeCorte Pedaco, Retangulo Regiao, int Quantidade)? ProcurarBloco(PadraoDePecasIguais padrao, int quantidade)
    {
        (PedacoDeCorte, Retangulo, int)? melhor = null;
        var melhorNota = (N: 0, Area: double.MaxValue, Cortes: int.MaxValue, Y: double.MaxValue, X: double.MaxValue);
        foreach (var pedaco in _livres)
        {
            foreach (var espaco in pedaco.Espacos)
            {
                var n = Math.Min(quantidade, padrao.Quantas(espaco.Comprimento, espaco.Largura));
                if (n == 0 || n < melhorNota.N)
                    continue;
                if (padrao.MenorRegiao(espaco.Comprimento, espaco.Largura, n) is not { } tamanho)
                    continue;
                foreach (var x in new[] { espaco.X, espaco.Direita - tamanho.X })
                {
                    foreach (var y in new[] { espaco.Y, espaco.Topo - tamanho.Y })
                    {
                        if (Ajustar(pedaco.Area, espaco, x, y, tamanho.X, tamanho.Y) is not { } regiao)
                            continue;
                        var nota = (n, espaco.Area, Cortes(pedaco.Area, regiao), regiao.Y, regiao.X);
                        if (melhor is null || MelhorBloco(nota, melhorNota))
                        {
                            melhor = (pedaco, regiao, n);
                            melhorNota = nota;
                        }
                    }
                }
            }
        }
        return melhor;
    }

    private static bool MelhorBloco((int N, double Area, int Cortes, double Y, double X) a,
        (int N, double Area, int Cortes, double Y, double X) b)
    {
        if (a.N != b.N)
            return a.N > b.N;
        // Mesma quantidade: no menor espaço (guarda os grandes para as outras peças)
        if (Math.Abs(a.Area - b.Area) > Eps)
            return a.Area < b.Area;
        if (a.Cortes != b.Cortes)
            return a.Cortes < b.Cortes;
        if (Math.Abs(a.Y - b.Y) > Eps)
            return a.Y < b.Y;
        return a.X < b.X - Eps;
    }

    /// <summary>
    /// Separa a região do bloco e corta nela as <paramref name="quantidade"/> peças iguais, como o padrão manda.
    /// Devolve onde cada peça ficou e se está girada.
    /// </summary>
    public List<(Retangulo Area, bool Girada)> ColocarBloco(PedacoDeCorte pedaco, Retangulo regiao, PadraoDePecasIguais padrao,
        int quantidade, RegraDeDivisao divisao, int primeiraPeca)
    {
        _livres.Remove(pedaco);
        var bloco = Isolar(pedaco, regiao, divisao);
        var saida = new List<(Retangulo, bool)>();
        var faltam = quantidade;
        var indice = primeiraPeca;
        Aplicar(bloco, padrao, ref faltam, ref indice, saida);
        return saida;
    }

    /// <summary>Corta o pedaço como o padrão manda; o que sobrar fica livre.</summary>
    private void Aplicar(PedacoDeCorte pedaco, PadraoDePecasIguais padrao, ref int faltam, ref int indice,
        List<(Retangulo, bool)> saida)
    {
        if (faltam == 0 || padrao.Passo(pedaco.Area.Comprimento, pedaco.Area.Largura) is not { } passo)
        {
            _livres.Add(pedaco);
            return;
        }

        if (passo is PassoDoPadrao.Corte corte)
        {
            var (primeiro, segundo) = Dividir(pedaco, corte.Direcao, corte.Distancia);
            Aplicar(primeiro!, padrao, ref faltam, ref indice, saida);
            if (segundo is not null)
                Aplicar(segundo, padrao, ref faltam, ref indice, saida);
            return;
        }

        // Grade: faixas deitadas (uma linha de peças cada), e cada faixa cortada em peças
        var grade = (PassoDoPadrao.Grade)passo;
        PedacoDeCorte? resto = pedaco;
        for (var linha = 0; linha < grade.Linhas && resto is not null && faltam > 0; linha++)
        {
            PedacoDeCorte? faixa;
            (faixa, resto) = resto.Area.Largura > grade.Largura + Eps
                ? Dividir(resto, DirecaoCorte.Horizontal, grade.Largura)
                : (resto, null);

            var restoDaFaixa = faixa;
            for (var coluna = 0; coluna < grade.Colunas && restoDaFaixa is not null && faltam > 0; coluna++)
            {
                PedacoDeCorte? celula;
                (celula, restoDaFaixa) = restoDaFaixa.Area.Comprimento > grade.Comprimento + Eps
                    ? Dividir(restoDaFaixa, DirecaoCorte.Vertical, grade.Comprimento)
                    : (restoDaFaixa, null);
                celula!.Peca = indice++;
                saida.Add((celula.Area, grade.Girada));
                faltam--;
            }
            if (restoDaFaixa is not null)
                _livres.Add(restoDaFaixa);
        }
        if (resto is not null)
            _livres.Add(resto);
    }

    /// <summary>
    /// Separa do pedaço o retângulo <paramref name="lugar"/> com até quatro cortes (antes e depois dele, nos
    /// dois sentidos). As sobras ficam livres; devolve o pedaço que ficou exatamente no lugar.
    /// </summary>
    private PedacoDeCorte Isolar(PedacoDeCorte pedaco, Retangulo lugar, RegraDeDivisao divisao)
    {
        var a = pedaco.Area;
        var k = _espessura;
        var (w, h) = (lugar.Comprimento, lugar.Largura);

        var horizontalPrimeiro = divisao switch
        {
            RegraDeDivisao.FaixasHorizontais => true,
            RegraDeDivisao.FaixasVerticais => false,
            RegraDeDivisao.MaiorSobra =>
                Math.Max(Area(a.Comprimento - w - k, h), Area(a.Comprimento, a.Largura - h - k))
                >= Math.Max(Area(a.Comprimento - w - k, a.Largura), Area(w, a.Largura - h - k)),
            _ => a.Comprimento - w <= a.Largura - h,
        };

        var atual = pedaco;
        if (horizontalPrimeiro)
        {
            atual = Separar(atual, DirecaoCorte.Horizontal, lugar.Y, h);
            atual = Separar(atual, DirecaoCorte.Vertical, lugar.X, w);
        }
        else
        {
            atual = Separar(atual, DirecaoCorte.Vertical, lugar.X, w);
            atual = Separar(atual, DirecaoCorte.Horizontal, lugar.Y, h);
        }
        return atual;
    }

    /// <summary>
    /// Corta antes (se a peça não começa na borda) e depois dela (se não termina na borda), numa direção.
    /// Devolve o pedaço onde a peça ficou.
    /// </summary>
    private PedacoDeCorte Separar(PedacoDeCorte pedaco, DirecaoCorte direcao, double inicioDaPeca, double tamanho)
    {
        var k = _espessura;
        var a = pedaco.Area;
        var (borda, fim) = direcao == DirecaoCorte.Vertical ? (a.X, a.Direita) : (a.Y, a.Topo);

        if (inicioDaPeca > borda + Eps)
        {
            // O corte fica logo antes da peça; a peça segue na segunda parte
            pedaco = Cortar(pedaco, direcao, Math.Max(0, inicioDaPeca - k - borda), pecaNaPrimeira: false);
        }
        if (inicioDaPeca + tamanho < fim - Eps)
            pedaco = Cortar(pedaco, direcao, inicioDaPeca + tamanho - (direcao == DirecaoCorte.Vertical ? pedaco.Area.X : pedaco.Area.Y),
                pecaNaPrimeira: true);
        return pedaco;
    }

    /// <summary>
    /// Corta o pedaço a <paramref name="distancia"/> da borda esquerda/de baixo. A parte sem a peça
    /// fica livre; devolve a parte com a peça.
    /// </summary>
    private PedacoDeCorte Cortar(PedacoDeCorte pedaco, DirecaoCorte direcao, double distancia, bool pecaNaPrimeira)
    {
        Dividir(pedaco, direcao, distancia);
        var comPeca = pecaNaPrimeira ? pedaco.Primeiro : pedaco.Segundo;
        var livre = pecaNaPrimeira ? pedaco.Segundo : pedaco.Primeiro;
        if (livre is not null)
            _livres.Add(livre);
        return comPeca!;
    }

    /// <summary>Corta o pedaço a <paramref name="distancia"/> da borda esquerda/de baixo e devolve as duas partes.</summary>
    private (PedacoDeCorte? Primeiro, PedacoDeCorte? Segundo) Dividir(PedacoDeCorte pedaco, DirecaoCorte direcao, double distancia)
    {
        var a = pedaco.Area;
        var k = _espessura;
        pedaco.Direcao = direcao;
        pedaco.Distancia = distancia;

        Retangulo? primeira, segunda;
        if (direcao == DirecaoCorte.Vertical)
        {
            pedaco.Posicao = a.X + distancia;
            var resto = a.Comprimento - distancia - k;
            primeira = distancia > Eps ? new Retangulo(a.X, a.Y, distancia, a.Largura) : null;
            segunda = resto > Eps ? new Retangulo(a.X + distancia + k, a.Y, resto, a.Largura) : null;
        }
        else
        {
            pedaco.Posicao = a.Y + distancia;
            var resto = a.Largura - distancia - k;
            primeira = distancia > Eps ? new Retangulo(a.X, a.Y, a.Comprimento, distancia) : null;
            segunda = resto > Eps ? new Retangulo(a.X, a.Y + distancia + k, a.Comprimento, resto) : null;
        }

        pedaco.Primeiro = primeira is { } p ? new PedacoDeCorte(p, Recortar(pedaco.Obstaculos, p)) : null;
        pedaco.Segundo = segunda is { } s ? new PedacoDeCorte(s, Recortar(pedaco.Obstaculos, s)) : null;
        return (pedaco.Primeiro, pedaco.Segundo);
    }

    /// <summary>Os obstáculos que caem dentro do retângulo, cortados na borda dele.</summary>
    private static IReadOnlyList<Retangulo> Recortar(IReadOnlyList<Retangulo> obstaculos, Retangulo area)
    {
        if (obstaculos.Count == 0)
            return obstaculos;
        var dentro = new List<Retangulo>();
        foreach (var o in obstaculos)
        {
            var x0 = Math.Max(o.X, area.X);
            var y0 = Math.Max(o.Y, area.Y);
            var x1 = Math.Min(o.Direita, area.Direita);
            var y1 = Math.Min(o.Topo, area.Topo);
            if (x1 - x0 > Eps && y1 - y0 > Eps)
                dentro.Add(new Retangulo(x0, y0, x1 - x0, y1 - y0));
        }
        return dentro;
    }

    private static double Area(double w, double h) => w > 0 && h > 0 ? w * h : 0;
}
