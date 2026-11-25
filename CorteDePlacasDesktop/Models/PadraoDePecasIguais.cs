using System;
using System.Collections.Generic;

namespace CorteDePlacasDesktop.Models;

/// <summary>
/// Melhor jeito de arrumar várias peças iguais num retângulo, misturando peças deitadas e em pé
/// (girar algumas costuma aproveitar a faixa que sobra). Conta exata entre os arranjos que a serra
/// consegue cortar (cortes de ponta a ponta): o retângulo vira uma grade de peças iguais, ou é dividido
/// por um corte em dois retângulos, cada um com o seu melhor arranjo.
/// </summary>
/// <remarks>
/// Só interessam medidas que são somas de peças (a·comprimento + b·largura, com o corte entre elas):
/// qualquer retângulo funciona como a maior dessas medidas que cabe nele. A tabela é feita uma vez
/// por tipo de peça e serve para qualquer espaço da chapa.
/// </remarks>
internal sealed class PadraoDePecasIguais
{
    private const double Eps = 1e-6;

    /// <summary>Acima disso a tabela completa demora demais: usa só medidas de faixas de peças iguais.</summary>
    private const long LimiteDeContas = 25_000_000;

    private enum Tipo : byte
    {
        Nada,
        GradeDeitada,
        GradeEmPe,
        CorteVertical,
        CorteHorizontal,
    }

    private readonly double _w;
    private readonly double _h;
    private readonly double _k;
    private readonly double[] _rx;
    private readonly double[] _ry;
    private readonly int[,] _quantas;
    private readonly Tipo[,] _tipo;
    private readonly int[,] _onde;

    /// <param name="comprimento">Medida da peça em X (sem girar).</param>
    /// <param name="largura">Medida da peça em Y (sem girar).</param>
    /// <param name="espessura">Material perdido entre duas peças.</param>
    public PadraoDePecasIguais(double comprimento, double largura, double espessura, double maxX, double maxY)
    {
        _w = comprimento;
        _h = largura;
        _k = espessura;
        _rx = Medidas(maxX, completas: true);
        _ry = Medidas(maxY, completas: true);
        if ((long)_rx.Length * _ry.Length * (_rx.Length + _ry.Length) / 2 > LimiteDeContas)
        {
            _rx = Medidas(maxX, completas: false);
            _ry = Medidas(maxY, completas: false);
        }

        _quantas = new int[_rx.Length, _ry.Length];
        _tipo = new Tipo[_rx.Length, _ry.Length];
        _onde = new int[_rx.Length, _ry.Length];
        Calcular();
    }

    /// <summary>Quantas peças cabem num espaço x × y.</summary>
    public int Quantas(double x, double y)
    {
        var i = Indice(_rx, x);
        var j = Indice(_ry, y);
        return i < 0 || j < 0 ? 0 : _quantas[i, j];
    }

    /// <summary>
    /// O menor pedaço (canto do espaço x × y) onde cabem <paramref name="n"/> peças; o resto do espaço
    /// fica livre para outras peças.
    /// </summary>
    public (double X, double Y)? MenorRegiao(double x, double y, int n)
    {
        var maxI = Indice(_rx, x);
        var maxJ = Indice(_ry, y);
        if (maxI < 0 || maxJ < 0 || _quantas[maxI, maxJ] < n)
            return null;

        (double X, double Y)? melhor = null;
        var melhorArea = double.MaxValue;
        var melhorSobra = -1.0;
        for (var i = 0; i <= maxI; i++)
        {
            if (_quantas[i, maxJ] < n)
                continue;
            // Menor altura com pelo menos n peças nesta largura (a conta só cresce com a altura)
            int lo = 0, hi = maxJ;
            while (lo < hi)
            {
                var meio = (lo + hi) / 2;
                if (_quantas[i, meio] >= n)
                    hi = meio;
                else
                    lo = meio + 1;
            }
            var area = _rx[i] * _ry[lo];
            var sobra = Math.Max(x - _rx[i], y - _ry[lo]);
            if (area < melhorArea - Eps || (Math.Abs(area - melhorArea) <= Eps && sobra > melhorSobra))
            {
                melhor = (_rx[i], _ry[lo]);
                melhorArea = area;
                melhorSobra = sobra;
            }
        }
        return melhor;
    }

    /// <summary>Onde ficam as primeiras <paramref name="n"/> peças do arranjo de um espaço x × y que começa em (x0, y0).</summary>
    public List<(Retangulo Area, bool Girada)> Posicoes(double x0, double y0, double x, double y, int n)
    {
        var saida = new List<(Retangulo, bool)>();
        Folhas(x0, y0, x, y, n, saida);
        return saida;
    }

    /// <summary>O primeiro passo do arranjo de um espaço x × y: uma grade de peças ou um corte.</summary>
    public PassoDoPadrao? Passo(double x, double y)
    {
        var i = Indice(_rx, x);
        var j = Indice(_ry, y);
        if (i < 0 || j < 0 || _quantas[i, j] == 0)
            return null;
        return _tipo[i, j] switch
        {
            Tipo.GradeDeitada => new PassoDoPadrao.Grade(_w, _h, false, Cabem(_rx[i], _w), Cabem(_ry[j], _h)),
            Tipo.GradeEmPe => new PassoDoPadrao.Grade(_h, _w, true, Cabem(_rx[i], _h), Cabem(_ry[j], _w)),
            Tipo.CorteVertical => new PassoDoPadrao.Corte(DirecaoCorte.Vertical, _rx[_onde[i, j]]),
            Tipo.CorteHorizontal => new PassoDoPadrao.Corte(DirecaoCorte.Horizontal, _ry[_onde[i, j]]),
            _ => null,
        };
    }

    private void Folhas(double x0, double y0, double x, double y, int n, List<(Retangulo, bool)> saida)
    {
        if (saida.Count >= n || Passo(x, y) is not { } passo)
            return;
        switch (passo)
        {
            case PassoDoPadrao.Grade g:
                for (var linha = 0; linha < g.Linhas; linha++)
                {
                    for (var coluna = 0; coluna < g.Colunas; coluna++)
                    {
                        if (saida.Count >= n)
                            return;
                        saida.Add((new Retangulo(x0 + coluna * (g.Comprimento + _k), y0 + linha * (g.Largura + _k),
                            g.Comprimento, g.Largura), g.Girada));
                    }
                }
                break;
            case PassoDoPadrao.Corte { Direcao: DirecaoCorte.Vertical } c:
                Folhas(x0, y0, c.Distancia, y, n, saida);
                Folhas(x0 + c.Distancia + _k, y0, x - c.Distancia - _k, y, n, saida);
                break;
            case PassoDoPadrao.Corte c:
                Folhas(x0, y0, x, c.Distancia, n, saida);
                Folhas(x0, y0 + c.Distancia + _k, x, y - c.Distancia - _k, n, saida);
                break;
        }
    }

    private void Calcular()
    {
        for (var i = 0; i < _rx.Length; i++)
        {
            var x = _rx[i];
            for (var j = 0; j < _ry.Length; j++)
            {
                var y = _ry[j];
                var melhor = Cabem(x, _w) * Cabem(y, _h);
                var tipo = melhor > 0 ? Tipo.GradeDeitada : Tipo.Nada;
                var onde = 0;

                var emPe = Cabem(x, _h) * Cabem(y, _w);
                if (emPe > melhor)
                {
                    melhor = emPe;
                    tipo = Tipo.GradeEmPe;
                }

                // Um corte vertical: a parte da esquerda até a metade (a outra metade é o mesmo caso ao contrário)
                for (var a = 0; a < i && _rx[a] <= (x - _k) / 2 + Eps; a++)
                {
                    var b = Indice(_rx, x - _rx[a] - _k);
                    if (b < 0)
                        continue;
                    var soma = _quantas[a, j] + _quantas[b, j];
                    if (soma > melhor)
                    {
                        melhor = soma;
                        tipo = Tipo.CorteVertical;
                        onde = a;
                    }
                }
                for (var a = 0; a < j && _ry[a] <= (y - _k) / 2 + Eps; a++)
                {
                    var b = Indice(_ry, y - _ry[a] - _k);
                    if (b < 0)
                        continue;
                    var soma = _quantas[i, a] + _quantas[i, b];
                    if (soma > melhor)
                    {
                        melhor = soma;
                        tipo = Tipo.CorteHorizontal;
                        onde = a;
                    }
                }

                _quantas[i, j] = melhor;
                _tipo[i, j] = tipo;
                _onde[i, j] = onde;
            }
        }
    }

    /// <summary>Quantas peças de medida <paramref name="peca"/> cabem enfileiradas em <paramref name="espaco"/>.</summary>
    private int Cabem(double espaco, double peca) =>
        peca <= 0 ? 0 : Math.Max(0, (int)Math.Floor((espaco + _k + Eps) / (peca + _k)));

    /// <summary>
    /// Medidas possíveis num eixo até <paramref name="max"/>: somas de peças deitadas e em pé com o corte entre elas
    /// (ou, se forem demais, só as de peças iguais enfileiradas).
    /// </summary>
    private double[] Medidas(double max, bool completas)
    {
        var medidas = new SortedSet<double>();
        var passoW = _w + _k;
        var passoH = _h + _k;
        for (var a = 0; a * passoW <= max + _k + Eps; a++)
        {
            for (var b = 0; a * passoW + b * passoH <= max + _k + Eps; b++)
            {
                if (!completas && a > 0 && b > 0)
                    break;
                if (a + b > 0)
                    medidas.Add(Math.Round(a * passoW + b * passoH - _k, 6));
            }
        }
        var lista = new List<double>();
        foreach (var m in medidas)
        {
            if (m <= max + Eps)
                lista.Add(m);
        }
        return lista.ToArray();
    }

    /// <summary>Posição da maior medida que não passa de <paramref name="valor"/> (ou -1).</summary>
    private static int Indice(double[] medidas, double valor)
    {
        int lo = 0, hi = medidas.Length - 1, achou = -1;
        while (lo <= hi)
        {
            var meio = (lo + hi) / 2;
            if (medidas[meio] <= valor + Eps)
            {
                achou = meio;
                lo = meio + 1;
            }
            else
            {
                hi = meio - 1;
            }
        }
        return achou;
    }
}

/// <summary>Um passo do arranjo de peças iguais.</summary>
internal abstract record PassoDoPadrao
{
    /// <summary>Grade de peças iguais, todas deitadas ou todas em pé, a partir do canto de baixo à esquerda.</summary>
    public sealed record Grade(double Comprimento, double Largura, bool Girada, int Colunas, int Linhas) : PassoDoPadrao;

    /// <summary>Um corte que divide o espaço; cada parte tem o seu arranjo.</summary>
    public sealed record Corte(DirecaoCorte Direcao, double Distancia) : PassoDoPadrao;
}
