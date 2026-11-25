using System;
using System.Collections.Generic;
using System.Linq;

namespace CorteDePlacasDesktop.Models;

public enum TipoCorte
{
    /// <summary>Seccionadora/esquadrejadeira: cada corte atravessa o pedaço inteiro (guilhotina).</summary>
    Serra,

    /// <summary>CNC/router: as peças podem ficar em qualquer posição.</summary>
    Cnc,
}

/// <summary>Uma peça pedida pelo operador (com a quantidade).</summary>
/// <param name="Comprimento">Medida em X (para peça desenhada, a medida externa do desenho).</param>
/// <param name="Largura">Medida em Y.</param>
/// <param name="Formato">Desenho da peça (começando em 0,0), ou null para retângulo.</param>
public sealed record PecaPedida(int Numero, string Descricao, double Comprimento, double Largura, int Quantidade,
    FormatoChapa? Formato = null)
{
    /// <summary>Área real de uma peça (mm²).</summary>
    public double AreaUnitaria => Formato?.Area ?? Comprimento * Largura;
}

/// <summary>Uma peça colocada numa chapa.</summary>
/// <param name="Area">Onde ela fica na chapa (já girada, se for o caso).</param>
/// <param name="Girada">Se foi girada 90° em relação ao que o operador informou.</param>
public sealed record PecaNaChapa(int Numero, int Copia, string Descricao, Retangulo Area, bool Girada,
    FormatoChapa? Formato, double AreaReal)
{
    /// <summary>Medidas como o operador informou (comprimento × largura), mesmo se girada.</summary>
    public string Medidas => Girada
        ? $"{Area.Largura:0.#} × {Area.Comprimento:0.#}"
        : $"{Area.Comprimento:0.#} × {Area.Largura:0.#}";

    /// <summary>
    /// Contorno real da peça já na posição da chapa (peça desenhada, girada se for o caso),
    /// ou null para peça retangular.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Ponto>>? ContornoNaChapa()
    {
        if (Formato is null)
            return null;
        var largOriginal = Formato.Limites.Largura;
        return Formato.Regiao
            .Select(c => (IReadOnlyList<Ponto>)c.Select(p => Girada
                    ? new Ponto(Area.X + (largOriginal - (p.Y - Formato.Limites.Y)), Area.Y + (p.X - Formato.Limites.X))
                    : new Ponto(Area.X + (p.X - Formato.Limites.X), Area.Y + (p.Y - Formato.Limites.Y)))
                .ToArray())
            .ToArray();
    }
}

public enum DirecaoCorte
{
    /// <summary>Linha de cima a baixo (X constante).</summary>
    Vertical,

    /// <summary>Linha da esquerda à direita (Y constante).</summary>
    Horizontal,
}

public enum TipoDePasso
{
    Refilo,
    SepararBloco,
    Corte,
}

/// <summary>
/// Um corte de serra: atravessa o <see cref="Pedaco"/> de ponta a ponta.
/// A linha fica em <see cref="Posicao"/> (X para vertical, Y para horizontal), de <see cref="Inicio"/> a <see cref="Fim"/>.
/// </summary>
public sealed record CorteSerra(int Ordem, TipoDePasso Tipo, DirecaoCorte Direcao, double Posicao, double Inicio, double Fim,
    Retangulo Pedaco, double Distancia, string Texto);

/// <summary>Uma chapa do plano: as peças que vão nela e (na serra) a ordem dos cortes.</summary>
public sealed record ChapaDoPlano(int Numero, IReadOnlyList<PecaNaChapa> Pecas, IReadOnlyList<CorteSerra> Cortes, double AreaChapa)
{
    public double AreaPecas => Pecas.Sum(p => p.AreaReal);

    /// <summary>Fração da chapa que vira peça (0 a 1).</summary>
    public double Aproveitamento => AreaChapa > 0 ? AreaPecas / AreaChapa : 0;
}

/// <summary>Resultado do cálculo.</summary>
public sealed record ResultadoPlano(
    TipoCorte Tipo,
    FormatoChapa Chapa,
    double Refilo,
    double Espessura,
    IReadOnlyList<ChapaDoPlano> Chapas,
    IReadOnlyList<(PecaPedida Peca, int QuantidadeQueNaoCoube)> NaoCouberam)
{
    public int TotalPecas => Chapas.Sum(c => c.Pecas.Count);

    public double AproveitamentoMedio =>
        Chapas.Count == 0 ? 0 : Chapas.Sum(c => c.AreaPecas) / Chapas.Sum(c => c.AreaChapa);
}
