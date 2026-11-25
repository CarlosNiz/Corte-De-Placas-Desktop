using System;
using System.Collections.Generic;

namespace CorteDePlacasDesktop.Models;

/// <summary>
/// Ponto na chapa, em milímetros. X cresce para a direita (ao longo do comprimento da chapa)
/// e Y cresce para cima (ao longo da largura), com origem no canto inferior esquerdo.
/// </summary>
public readonly record struct Ponto(double X, double Y);

/// <summary>
/// Retângulo alinhado aos eixos, em milímetros. <see cref="Comprimento"/> é medido em X
/// e <see cref="Largura"/> em Y, como na chapa.
/// </summary>
public readonly record struct Retangulo(double X, double Y, double Comprimento, double Largura)
{
    public double Direita => X + Comprimento;

    public double Topo => Y + Largura;

    public double Area => Comprimento * Largura;

    public Ponto Centro => new(X + Comprimento / 2, Y + Largura / 2);

    /// <summary>Se os interiores se sobrepõem (encostar a borda não conta).</summary>
    public bool Sobrepoe(Retangulo outro) =>
        X < outro.Direita && outro.X < Direita && Y < outro.Topo && outro.Y < Topo;

    public Retangulo Expandido(double margem) =>
        new(X - margem, Y - margem, Comprimento + 2 * margem, Largura + 2 * margem);

    public static Retangulo EntrePontos(Ponto a, Ponto b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>Contas de geometria usadas pela chapa.</summary>
public static class Geometria
{
    private const double Tolerancia = 1e-6;

    /// <summary>Área com sinal (positiva quando os pontos estão no sentido anti-horário).</summary>
    public static double AreaComSinal(IReadOnlyList<Ponto> poligono)
    {
        double soma = 0;
        for (var i = 0; i < poligono.Count; i++)
        {
            var a = poligono[i];
            var b = poligono[(i + 1) % poligono.Count];
            soma += a.X * b.Y - b.X * a.Y;
        }
        return soma / 2;
    }

    public static Retangulo Limites(IEnumerable<Ponto> pontos)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pontos)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        return minX > maxX ? default : new Retangulo(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Se o ponto está dentro do polígono (regra par-ímpar).</summary>
    public static bool PontoDentro(IReadOnlyList<Ponto> poligono, Ponto p)
    {
        var dentro = false;
        for (int i = 0, j = poligono.Count - 1; i < poligono.Count; j = i++)
        {
            var a = poligono[i];
            var b = poligono[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                dentro = !dentro;
            }
        }
        return dentro;
    }

    /// <summary>
    /// Se o ponto está dentro de uma região formada por vários contornos (partes e furos),
    /// pela regra par-ímpar: dentro de um furo conta como fora.
    /// </summary>
    public static bool PontoDentroRegiao(IReadOnlyList<IReadOnlyList<Ponto>> regiao, Ponto p)
    {
        var dentro = false;
        foreach (var contorno in regiao)
        {
            if (PontoDentro(contorno, p))
                dentro = !dentro;
        }
        return dentro;
    }

    /// <summary>Se o retângulo inteiro está dentro da região (pode encostar nas bordas).</summary>
    public static bool RetanguloDentroRegiao(IReadOnlyList<IReadOnlyList<Ponto>> regiao, Retangulo r)
    {
        // Se nenhuma borda da região atravessa o interior do retângulo, o interior está
        // todo dentro ou todo fora; o centro decide qual dos dois.
        foreach (var contorno in regiao)
        {
            for (var i = 0; i < contorno.Count; i++)
            {
                var a = contorno[i];
                var b = contorno[(i + 1) % contorno.Count];
                // Descarte rápido: borda longe do retângulo
                if (Math.Max(a.X, b.X) <= r.X || Math.Min(a.X, b.X) >= r.Direita ||
                    Math.Max(a.Y, b.Y) <= r.Y || Math.Min(a.Y, b.Y) >= r.Topo)
                {
                    continue;
                }
                if (SegmentoAtravessaInterior(a, b, r))
                    return false;
            }
        }
        return PontoDentroRegiao(regiao, r.Centro);
    }

    /// <summary>Se algum trecho do segmento passa por dentro do retângulo (não só pela borda).</summary>
    public static bool SegmentoAtravessaInterior(Ponto a, Ponto b, Retangulo r)
    {
        const double folga = 1e-4;
        double xMin = r.X + folga, xMax = r.Direita - folga, yMin = r.Y + folga, yMax = r.Topo - folga;
        if (xMin >= xMax || yMin >= yMax)
            return false;

        // Recorte de Liang–Barsky
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t0 = 0, t1 = 1;
        Span<double> p = [-dx, dx, -dy, dy];
        Span<double> q = [a.X - xMin, xMax - a.X, a.Y - yMin, yMax - a.Y];
        for (var k = 0; k < 4; k++)
        {
            if (Math.Abs(p[k]) < Tolerancia)
            {
                if (q[k] < 0)
                    return false;
                continue;
            }
            var t = q[k] / p[k];
            if (p[k] < 0)
                t0 = Math.Max(t0, t);
            else
                t1 = Math.Min(t1, t);
            if (t0 > t1)
                return false;
        }
        return t1 - t0 > Tolerancia;
    }

    /// <summary>Se os segmentos ab e cd se tocam ou se cruzam.</summary>
    public static bool SegmentosSeTocam(Ponto a, Ponto b, Ponto c, Ponto d)
    {
        var o1 = Orientacao(a, b, c);
        var o2 = Orientacao(a, b, d);
        var o3 = Orientacao(c, d, a);
        var o4 = Orientacao(c, d, b);

        if (o1 != o2 && o3 != o4)
            return true;
        return (o1 == 0 && NoSegmento(a, c, b)) || (o2 == 0 && NoSegmento(a, d, b))
            || (o3 == 0 && NoSegmento(c, a, d)) || (o4 == 0 && NoSegmento(c, b, d));
    }

    /// <summary>Distância do ponto p ao segmento ab.</summary>
    public static double DistanciaAoSegmento(Ponto p, Ponto a, Ponto b, out Ponto maisProximo)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var comprimento2 = dx * dx + dy * dy;
        var t = comprimento2 < Tolerancia ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / comprimento2, 0, 1);
        maisProximo = new Ponto(a.X + t * dx, a.Y + t * dy);
        return Distancia(p, maisProximo);
    }

    public static double Distancia(Ponto a, Ponto b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static int Orientacao(Ponto a, Ponto b, Ponto c)
    {
        var v = (b.Y - a.Y) * (c.X - b.X) - (b.X - a.X) * (c.Y - b.Y);
        return Math.Abs(v) < Tolerancia ? 0 : v > 0 ? 1 : 2;
    }

    private static bool NoSegmento(Ponto a, Ponto p, Ponto b) =>
        p.X <= Math.Max(a.X, b.X) + Tolerancia && p.X >= Math.Min(a.X, b.X) - Tolerancia &&
        p.Y <= Math.Max(a.Y, b.Y) + Tolerancia && p.Y >= Math.Min(a.Y, b.Y) - Tolerancia;
}
