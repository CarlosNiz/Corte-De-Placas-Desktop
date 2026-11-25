using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Divide em retângulos a parte de <paramref name="limites"/> que fica FORA da região (o que não
    /// pode ser usado). Conservador: em bordas inclinadas ou curvas, a parte de fora fica um pouco
    /// maior que a real, nunca menor. Faixas verticais vizinhas iguais são juntadas.
    /// </summary>
    public static List<Retangulo> RetangulosFora(IReadOnlyList<IReadOnlyList<Ponto>> regiao, Retangulo limites)
    {
        const double eps = 1e-6;
        var xs = regiao.SelectMany(c => c.Select(p => p.X))
            .Append(limites.X).Append(limites.Direita)
            .Where(x => x >= limites.X - eps && x <= limites.Direita + eps)
            .Select(x => Math.Round(x, 6))
            .Distinct()
            .Order()
            .ToList();

        var resultado = new List<Retangulo>();
        List<(double Y0, double Y1)>? anteriores = null;
        var inicioDaFaixa = 0.0;
        var fimDaFaixa = 0.0;

        for (var i = 0; i + 1 < xs.Count; i++)
        {
            double x0 = xs[i], x1 = xs[i + 1];
            if (x1 - x0 < eps)
                continue;

            // Partes de dentro nesta faixa (valem para a faixa inteira) e, por complemento, as de fora.
            var dentro = IntervalosDentro(regiao, x0, x1);
            var fora = new List<(double, double)>();
            var y = limites.Y;
            foreach (var (a, b) in dentro)
            {
                if (a - y > eps)
                    fora.Add((y, a));
                y = Math.Max(y, b);
            }
            if (limites.Topo - y > eps)
                fora.Add((y, limites.Topo));

            if (anteriores is not null && Math.Abs(x0 - fimDaFaixa) < eps && MesmosIntervalos(anteriores, fora))
            {
                fimDaFaixa = x1;
                continue;
            }

            Fechar();
            anteriores = fora;
            inicioDaFaixa = x0;
            fimDaFaixa = x1;
        }
        Fechar();
        return resultado;

        void Fechar()
        {
            if (anteriores is null)
                return;
            foreach (var (a, b) in anteriores)
                resultado.Add(new Retangulo(inicioDaFaixa, a, fimDaFaixa - inicioDaFaixa, b - a));
        }
    }

    /// <summary>
    /// Intervalos em Y que ficam dentro da região em TODA a faixa x0..x1 (não há cantos dentro da faixa,
    /// então cada borda é uma reta; basta olhar as duas pontas).
    /// </summary>
    private static List<(double, double)> IntervalosDentro(IReadOnlyList<IReadOnlyList<Ponto>> regiao, double x0, double x1)
    {
        var meio = (x0 + x1) / 2;
        var cruzamentos = new List<(double YMeio, double Y0, double Y1)>();
        foreach (var contorno in regiao)
        {
            for (var i = 0; i < contorno.Count; i++)
            {
                var a = contorno[i];
                var b = contorno[(i + 1) % contorno.Count];
                if ((a.X < meio) == (b.X < meio))
                    continue;
                double YEm(double x) => a.Y + (x - a.X) * (b.Y - a.Y) / (b.X - a.X);
                cruzamentos.Add((YEm(meio), YEm(x0), YEm(x1)));
            }
        }
        cruzamentos.Sort((p, q) => p.YMeio.CompareTo(q.YMeio));

        var dentro = new List<(double, double)>();
        for (var i = 0; i + 1 < cruzamentos.Count; i += 2)
        {
            var baixo = cruzamentos[i];
            var cima = cruzamentos[i + 1];
            var y0 = Math.Max(baixo.Y0, baixo.Y1);
            var y1 = Math.Min(cima.Y0, cima.Y1);
            if (y1 > y0)
                dentro.Add((y0, y1));
        }
        return dentro;
    }

    private static bool MesmosIntervalos(List<(double, double)> a, List<(double, double)> b) =>
        a.Count == b.Count && a.Zip(b).All(p => Math.Abs(p.First.Item1 - p.Second.Item1) < 1e-6
                                                && Math.Abs(p.First.Item2 - p.Second.Item2) < 1e-6);

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
