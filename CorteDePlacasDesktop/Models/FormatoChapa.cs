using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace CorteDePlacasDesktop.Models;

/// <summary>
/// Formato de uma chapa: um contorno (qualquer polígono, inclusive com bordas inclinadas)
/// mais retângulos e círculos que somam ou tiram área. A placa de verdade é a
/// <see cref="Regiao"/>: contorno + formas que somam − formas que tiram. Medidas em milímetros.
/// Imutável: para mudar o formato, crie outro.
/// </summary>
public sealed class FormatoChapa
{
    /// <summary>Passo da busca por posição nas regiões sem canto próximo (bordas inclinadas, curvas).</summary>
    private const double PassoBusca = 50;

    private const double Tolerancia = 1e-6;

    private readonly Dictionary<(double, double, double), bool> _cacheCabe = new();
    private readonly Dictionary<double, IReadOnlyList<IReadOnlyList<Ponto>>> _cacheAreaUtil = new();
    private readonly Lazy<PathsD> _regiaoClipper;
    private string? _erro;
    private bool _validado;

    public FormatoChapa(IEnumerable<Ponto> contorno, IEnumerable<FormaChapa>? formas = null)
    {
        Contorno = contorno.ToArray();
        Formas = (formas ?? []).ToArray();

        var regiao = CalcularRegiao();
        Regiao = DeClipper(regiao);
        Partes = regiao.Count(p => Clipper.Area(p) > 0);
        Area = Math.Max(0, Clipper.Area(regiao));
        Limites = Geometria.Limites(Regiao.SelectMany(p => p));
        _regiaoClipper = new Lazy<PathsD>(() => ParaClipper(Regiao));
    }

    public static FormatoChapa RetanguloSimples(double comprimento, double largura) =>
        new([new(0, 0), new(comprimento, 0), new(comprimento, largura), new(0, largura)]);

    /// <summary>Cantos do contorno, em ordem (horária ou anti-horária).</summary>
    public IReadOnlyList<Ponto> Contorno { get; }

    /// <summary>Retângulos e círculos que somam ou tiram área.</summary>
    public IReadOnlyList<FormaChapa> Formas { get; }

    /// <summary>A placa de verdade: contornos externos e de furos (regra par-ímpar).</summary>
    public IReadOnlyList<IReadOnlyList<Ponto>> Regiao { get; }

    /// <summary>Em quantos pedaços separados a placa ficou (normalmente 1).</summary>
    public int Partes { get; }

    /// <summary>Menor retângulo que contém a placa (medidas externas).</summary>
    public Retangulo Limites { get; }

    /// <summary>Área da placa em mm².</summary>
    public double Area { get; }

    /// <summary>Retângulo comum, sem formas.</summary>
    public bool EhRetanguloSimples
    {
        get
        {
            if (Formas.Count != 0 || Contorno.Count != 4)
                return false;
            var limites = Geometria.Limites(Contorno);
            return Contorno.All(p => (Igual(p.X, limites.X) || Igual(p.X, limites.Direita))
                                     && (Igual(p.Y, limites.Y) || Igual(p.Y, limites.Topo)))
                   && Igual(Math.Abs(Geometria.AreaComSinal(Contorno)), limites.Area);
        }
    }

    /// <summary>Problema no formato que impede o uso, ou null se está tudo certo.</summary>
    public string? Validar()
    {
        if (!_validado)
        {
            _erro = CalcularErro();
            _validado = true;
        }
        return _erro;
    }

    /// <summary>
    /// Se uma peça de comprimento × largura cabe em algum lugar da placa sem girar,
    /// mantendo o refilo de distância de todas as bordas (inclusive de furos e recortes).
    /// </summary>
    public bool Cabe(double comprimento, double largura, double refilo)
    {
        if (comprimento <= 0 || largura <= 0)
            return false;

        var chave = (comprimento, largura, refilo);
        lock (_cacheCabe)
        {
            if (_cacheCabe.TryGetValue(chave, out var guardado))
                return guardado;
        }
        var cabe = Validar() is null && ProcurarPosicao(comprimento, largura, refilo) is not null;
        lock (_cacheCabe)
            _cacheCabe[chave] = cabe;
        return cabe;
    }

    /// <summary>
    /// Procura uma posição (canto inferior esquerdo da peça) onde ela caiba, começando por baixo
    /// e pela esquerda. Retorna null se não houver.
    /// </summary>
    public Ponto? ProcurarPosicao(double comprimento, double largura, double refilo)
    {
        var util = AreaUtil(refilo);
        var limites = Geometria.Limites(util.SelectMany(p => p));
        if (util.Count == 0 || comprimento > limites.Comprimento + Tolerancia || largura > limites.Largura + Tolerancia)
            return null;

        var vertices = util.SelectMany(p => p).ToList();
        var xs = Candidatos(vertices.Select(p => p.X), comprimento, limites.X, limites.Direita);
        var ys = Candidatos(vertices.Select(p => p.Y), largura, limites.Y, limites.Topo);

        foreach (var y in ys)
        {
            foreach (var x in xs)
            {
                if (Geometria.RetanguloDentroRegiao(util, new Retangulo(x, y, comprimento, largura)))
                    return new Ponto(x, y);
            }
        }
        return null;
    }

    /// <summary>A placa sem a faixa do refilo em volta de todas as bordas.</summary>
    public IReadOnlyList<IReadOnlyList<Ponto>> AreaUtil(double refilo)
    {
        if (refilo <= 0)
            return Regiao;
        lock (_cacheAreaUtil)
        {
            if (!_cacheAreaUtil.TryGetValue(refilo, out var util))
            {
                // Cantos em "quina" (miter): o refilo corta cada borda em linha reta, como na serra.
                var encolhida = Clipper.InflatePaths(ParaClipper(Regiao), -refilo, JoinType.Miter, EndType.Polygon, miterLimit: 10);
                util = DeClipper(encolhida);
                _cacheAreaUtil[refilo] = util;
            }
            return util;
        }
    }

    /// <summary>
    /// Se dentro do retângulo há alguma parte da placa (mais que 1 mm²). Serve para saber se um corte
    /// separa material de verdade ou só passaria pelo ar (fora da placa ou dentro de um furo).
    /// </summary>
    public bool TemMaterial(Retangulo r)
    {
        if (r.Comprimento <= 0 || r.Largura <= 0)
            return false;
        var retangulo = new PathsD
        {
            new PathD { new(r.X, r.Y), new(r.Direita, r.Y), new(r.Direita, r.Topo), new(r.X, r.Topo) },
        };
        var dentro = Clipper.Intersect(_regiaoClipper.Value, retangulo, FillRule.EvenOdd);
        return Math.Abs(Clipper.Area(dentro)) > 1.0;
    }

    /// <summary>Mesmo formato, deslocado para que o canto inferior esquerdo da placa fique em (0, 0).</summary>
    public FormatoChapa Normalizado()
    {
        double dx = Limites.X, dy = Limites.Y;
        return new FormatoChapa(
            Contorno.Select(p => new Ponto(Arredondar(p.X - dx), Arredondar(p.Y - dy))),
            Formas.Select(f => f with
            {
                Limites = new Retangulo(Arredondar(f.Limites.X - dx), Arredondar(f.Limites.Y - dy),
                    Arredondar(f.Limites.Comprimento), Arredondar(f.Limites.Largura)),
            }));
    }

    private PathsD CalcularRegiao()
    {
        if (Contorno.Count < 3)
            return [];

        var adicionar = ParaClipper(Formas.Where(f => f.Operacao == OperacaoForma.Adicionar && TemTamanho(f)).Select(f => f.Poligono()));
        var remover = ParaClipper(Formas.Where(f => f.Operacao == OperacaoForma.Remover && TemTamanho(f)).Select(f => f.Poligono()));

        var regiao = Clipper.Union(ParaClipper([Contorno]), adicionar, FillRule.NonZero);
        if (remover.Count > 0)
            regiao = Clipper.Difference(regiao, remover, FillRule.NonZero);
        return regiao;
    }

    private string? CalcularErro()
    {
        var n = Contorno.Count;
        if (n < 3)
            return "O contorno precisa de pelo menos 3 cantos.";

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            if (Geometria.Distancia(Contorno[i], Contorno[j]) < 0.5)
                return $"Os cantos {i + 1} e {j + 1} estão no mesmo lugar.";
        }

        for (var i = 0; i < n; i++)
        {
            // Duas bordas seguidas na mesma reta, uma voltando sobre a outra.
            var anterior = Contorno[(i + n - 1) % n];
            var atual = Contorno[i];
            var proximo = Contorno[(i + 1) % n];
            double ax = atual.X - anterior.X, ay = atual.Y - anterior.Y;
            double bx = proximo.X - atual.X, by = proximo.Y - atual.Y;
            if (Math.Abs(ax * by - ay * bx) < Tolerancia && ax * bx + ay * by < 0)
                return $"O contorno volta sobre ele mesmo no canto {i + 1}.";
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var vizinhas = j == i + 1 || (i == 0 && j == n - 1);
                if (vizinhas)
                    continue;
                if (Geometria.SegmentosSeTocam(Contorno[i], Contorno[(i + 1) % n], Contorno[j], Contorno[(j + 1) % n]))
                    return "As bordas da chapa se cruzam. Arraste os cantos para desfazer o cruzamento.";
            }
        }

        for (var i = 0; i < Formas.Count; i++)
        {
            if (!TemTamanho(Formas[i]))
                return $"A forma {i + 1} está sem tamanho.";
        }

        if (Area < 1)
            return "A placa ficou sem área.";

        return null;
    }

    /// <summary>
    /// Posições a testar num eixo: alinhadas aos cantos da área útil (onde as peças costumam
    /// encostar) e, entre elas, a cada <see cref="PassoBusca"/> mm para cobrir bordas inclinadas e curvas.
    /// </summary>
    private static List<double> Candidatos(IEnumerable<double> coordenadas, double tamanho, double minimo, double maximo)
    {
        var limite = maximo - tamanho;
        var valores = new List<double>();
        foreach (var c in coordenadas)
        {
            valores.Add(c);
            valores.Add(c - tamanho);
        }
        for (var v = minimo; v <= limite; v += PassoBusca)
            valores.Add(v);
        valores.Add(limite);

        return valores
            .Where(v => v >= minimo - Tolerancia && v <= limite + Tolerancia)
            .Select(v => Math.Round(Math.Clamp(v, minimo, limite), 2))
            .Distinct()
            .Order()
            .ToList();
    }

    private static bool TemTamanho(FormaChapa forma) => forma.Limites.Comprimento > 0 && forma.Limites.Largura > 0;

    private static PathsD ParaClipper(IEnumerable<IReadOnlyList<Ponto>> poligonos)
    {
        var caminhos = new PathsD();
        foreach (var poligono in poligonos)
        {
            var caminho = new PathD(poligono.Count);
            caminho.AddRange(poligono.Select(p => new PointD(p.X, p.Y)));
            caminhos.Add(caminho);
        }
        return caminhos;
    }

    private static IReadOnlyList<IReadOnlyList<Ponto>> DeClipper(PathsD caminhos) =>
        caminhos.Select(c => (IReadOnlyList<Ponto>)c.Select(p => new Ponto(p.x, p.y)).ToArray()).ToArray();

    private static bool Igual(double a, double b) => Math.Abs(a - b) < 1e-3;

    private static double Arredondar(double v) => Math.Round(v, 1);
}
