using System;
using System.Collections.Generic;
using System.Linq;

namespace CorteDePlacasDesktop.Models;

/// <summary>
/// Formato de uma chapa: o contorno (qualquer polígono, inclusive com bordas inclinadas)
/// e as áreas com defeito que não podem ser usadas. Medidas em milímetros.
/// Imutável: para mudar o formato, crie outro.
/// </summary>
public sealed class FormatoChapa
{
    /// <summary>Passo da busca por posição nas regiões sem canto próximo (bordas inclinadas).</summary>
    private const double PassoBusca = 50;

    private const double Tolerancia = 1e-6;

    private readonly Dictionary<(double, double, double), bool> _cacheCabe = new();
    private string? _erro;
    private bool _validado;

    public FormatoChapa(IEnumerable<Ponto> contorno, IEnumerable<Retangulo>? defeitos = null)
    {
        Contorno = contorno.ToArray();
        Defeitos = (defeitos ?? []).ToArray();
        Limites = Geometria.Limites(Contorno);

        var areaDefeitos = Defeitos.Sum(d => AreaDaIntersecao(d, Limites));
        Area = Math.Max(0, Math.Abs(Geometria.AreaComSinal(Contorno)) - areaDefeitos);
    }

    public static FormatoChapa RetanguloSimples(double comprimento, double largura) =>
        new([new(0, 0), new(comprimento, 0), new(comprimento, largura), new(0, largura)]);

    /// <summary>Cantos do contorno, em ordem (horária ou anti-horária).</summary>
    public IReadOnlyList<Ponto> Contorno { get; }

    public IReadOnlyList<Retangulo> Defeitos { get; }

    /// <summary>Menor retângulo que contém o contorno (medidas externas).</summary>
    public Retangulo Limites { get; }

    /// <summary>Área aproveitável em mm² (contorno menos defeitos).</summary>
    public double Area { get; }

    /// <summary>Retângulo comum, sem recortes nem defeitos.</summary>
    public bool EhRetanguloSimples =>
        Defeitos.Count == 0
        && Contorno.Count == 4
        && Contorno.All(p => (Igual(p.X, Limites.X) || Igual(p.X, Limites.Direita))
                             && (Igual(p.Y, Limites.Y) || Igual(p.Y, Limites.Topo)))
        && Igual(Math.Abs(Geometria.AreaComSinal(Contorno)), Limites.Area);

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
    /// Se uma peça de comprimento × largura cabe em algum lugar da chapa sem girar,
    /// mantendo o refilo de distância das bordas e sem pegar nenhum defeito.
    /// </summary>
    public bool Cabe(double comprimento, double largura, double refilo)
    {
        if (comprimento <= 0 || largura <= 0)
            return false;

        var chave = (comprimento, largura, refilo);
        if (!_cacheCabe.TryGetValue(chave, out var cabe))
        {
            cabe = Validar() is null && ProcurarPosicao(comprimento, largura, refilo) is not null;
            _cacheCabe[chave] = cabe;
        }
        return cabe;
    }

    /// <summary>
    /// Procura uma posição (canto inferior esquerdo da peça) onde ela caiba, começando por baixo
    /// e pela esquerda. Retorna null se não houver.
    /// </summary>
    public Ponto? ProcurarPosicao(double comprimento, double largura, double refilo)
    {
        // A peça com o refilo em volta precisa ficar dentro do contorno.
        var envolvente = new Retangulo(0, 0, comprimento + 2 * refilo, largura + 2 * refilo);
        if (envolvente.Comprimento > Limites.Comprimento + Tolerancia || envolvente.Largura > Limites.Largura + Tolerancia)
            return null;

        var xs = Candidatos(
            Contorno.Select(p => p.X),
            Defeitos.SelectMany(d => new[] { d.Direita - refilo, d.X - refilo - comprimento }),
            envolvente.Comprimento, Limites.X, Limites.Direita);
        var ys = Candidatos(
            Contorno.Select(p => p.Y),
            Defeitos.SelectMany(d => new[] { d.Topo - refilo, d.Y - refilo - largura }),
            envolvente.Largura, Limites.Y, Limites.Topo);

        foreach (var y in ys)
        {
            foreach (var x in xs)
            {
                var peca = new Retangulo(x + refilo, y + refilo, comprimento, largura);
                if (Defeitos.Any(d => d.Sobrepoe(peca)))
                    continue;
                if (Geometria.RetanguloDentro(Contorno, envolvente with { X = x, Y = y }))
                    return new Ponto(peca.X, peca.Y);
            }
        }
        return null;
    }

    /// <summary>Mesmo formato, deslocado para que o canto inferior esquerdo fique em (0, 0).</summary>
    public FormatoChapa Normalizado()
    {
        double dx = Limites.X, dy = Limites.Y;
        return new FormatoChapa(
            Contorno.Select(p => new Ponto(Arredondar(p.X - dx), Arredondar(p.Y - dy))),
            Defeitos.Select(d => new Retangulo(Arredondar(d.X - dx), Arredondar(d.Y - dy),
                Arredondar(d.Comprimento), Arredondar(d.Largura))));
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

        if (Math.Abs(Geometria.AreaComSinal(Contorno)) < 1)
            return "O contorno não tem área.";

        for (var i = 0; i < Defeitos.Count; i++)
        {
            if (Defeitos[i].Comprimento <= 0 || Defeitos[i].Largura <= 0)
                return $"O defeito {i + 1} está sem tamanho.";
        }

        return null;
    }

    /// <summary>
    /// Posições a testar num eixo: alinhadas aos cantos e aos defeitos (onde as peças costumam
    /// encostar) e, entre elas, a cada <see cref="PassoBusca"/> mm para cobrir bordas inclinadas.
    /// </summary>
    private static List<double> Candidatos(IEnumerable<double> coordenadasCantos, IEnumerable<double> extras,
        double tamanho, double minimo, double maximo)
    {
        var limite = maximo - tamanho;
        var valores = new List<double>();
        foreach (var c in coordenadasCantos)
        {
            valores.Add(c);
            valores.Add(c - tamanho);
        }
        valores.AddRange(extras);
        for (var v = minimo; v <= limite; v += PassoBusca)
            valores.Add(v);
        valores.Add(limite);

        return valores
            .Where(v => v >= minimo - Tolerancia && v <= limite + Tolerancia)
            .Select(v => Math.Clamp(v, minimo, limite))
            .Distinct()
            .Order()
            .ToList();
    }

    private static double AreaDaIntersecao(Retangulo a, Retangulo b)
    {
        var comprimento = Math.Min(a.Direita, b.Direita) - Math.Max(a.X, b.X);
        var largura = Math.Min(a.Topo, b.Topo) - Math.Max(a.Y, b.Y);
        return comprimento > 0 && largura > 0 ? comprimento * largura : 0;
    }

    private static bool Igual(double a, double b) => Math.Abs(a - b) < 1e-3;

    private static double Arredondar(double v) => Math.Round(v, 1);
}
