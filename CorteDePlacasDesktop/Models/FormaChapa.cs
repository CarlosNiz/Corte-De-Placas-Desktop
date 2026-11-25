using System;
using System.Collections.Generic;

namespace CorteDePlacasDesktop.Models;

public enum TipoForma
{
    Retangulo,
    Circulo,
}

public enum OperacaoForma
{
    /// <summary>Tira essa área da placa (furo, recorte, canto quebrado, defeito...).</summary>
    Remover,

    /// <summary>Soma essa área à placa.</summary>
    Adicionar,
}

/// <summary>
/// Retângulo ou círculo colocado sobre a placa, que tira ou soma área.
/// Para o círculo, <see cref="Limites"/> é o quadrado que o envolve.
/// </summary>
public readonly record struct FormaChapa(TipoForma Tipo, OperacaoForma Operacao, Retangulo Limites)
{
    /// <summary>Quantos lados o círculo vira quando é transformado em polígono.</summary>
    public const int LadosDoCirculo = 48;

    public static FormaChapa Retangular(OperacaoForma operacao, Retangulo area) =>
        new(TipoForma.Retangulo, operacao, area);

    public static FormaChapa Circular(OperacaoForma operacao, Ponto centro, double diametro) =>
        new(TipoForma.Circulo, operacao,
            new Retangulo(centro.X - diametro / 2, centro.Y - diametro / 2, diametro, diametro));

    public Ponto Centro => Limites.Centro;

    public double Diametro => Limites.Comprimento;

    public bool Contem(Ponto p) => Tipo == TipoForma.Circulo
        ? Geometria.Distancia(p, Centro) <= Diametro / 2
        : p.X >= Limites.X && p.X <= Limites.Direita && p.Y >= Limites.Y && p.Y <= Limites.Topo;

    /// <summary>
    /// Contorno da forma como polígono. O círculo que tira área é feito um pouco maior que o real
    /// (polígono por fora) e o que soma, um pouco menor (por dentro): assim o cálculo nunca
    /// usa material que não existe.
    /// </summary>
    public IReadOnlyList<Ponto> Poligono()
    {
        if (Tipo == TipoForma.Retangulo)
        {
            return
            [
                new(Limites.X, Limites.Y), new(Limites.Direita, Limites.Y),
                new(Limites.Direita, Limites.Topo), new(Limites.X, Limites.Topo),
            ];
        }

        var raio = Diametro / 2;
        if (Operacao == OperacaoForma.Remover)
            raio /= Math.Cos(Math.PI / LadosDoCirculo);

        var pontos = new Ponto[LadosDoCirculo];
        for (var i = 0; i < LadosDoCirculo; i++)
        {
            var angulo = 2 * Math.PI * i / LadosDoCirculo;
            pontos[i] = new Ponto(Centro.X + raio * Math.Cos(angulo), Centro.Y + raio * Math.Sin(angulo));
        }
        return pontos;
    }

    public FormaChapa Deslocada(double dx, double dy) =>
        this with { Limites = Limites with { X = Limites.X + dx, Y = Limites.Y + dy } };
}
