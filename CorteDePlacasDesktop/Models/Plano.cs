using System;
using System.Collections.Generic;
using System.Linq;

namespace CorteDePlacasDesktop.Models;

/// <summary>Como as peças ficaram arrumadas na chapa (o cálculo escolhe a melhor).</summary>
public enum TipoCorte
{
    /// <summary>Cortes retos de ponta a ponta (guilhotina): serve para serra e para CNC, com a ordem dos cortes.</summary>
    Serra,

    /// <summary>Encaixe livre: a fresa da CNC contorna cada peça, que podem se encostar pela ponta.</summary>
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
    /// <summary>Aparar uma borda da chapa (antes das peças).</summary>
    Refilo,

    /// <summary>Corte que separa as peças.</summary>
    Corte,
}

/// <summary>
/// Um corte de serra: atravessa o <see cref="Pedaco"/> de ponta a ponta.
/// A linha fica em <see cref="Posicao"/> (X para vertical, Y para horizontal), de <see cref="Inicio"/> a <see cref="Fim"/>.
/// </summary>
public sealed record CorteSerra(int Ordem, TipoDePasso Tipo, DirecaoCorte Direcao, double Posicao, double Inicio, double Fim,
    Retangulo Pedaco, double Distancia, string Texto);

/// <summary>Uma chapa do plano: as peças que vão nela e (na serra) a ordem dos cortes.</summary>
/// <param name="Sobra">
/// O maior retângulo livre que sobra na chapa, já descontando o corte em volta das peças (para o operador
/// ver a medida que sobrou), ou null se a sobra for pequena demais para interessar.
/// </param>
public sealed record ChapaDoPlano(int Numero, IReadOnlyList<PecaNaChapa> Pecas, IReadOnlyList<CorteSerra> Cortes, double AreaChapa,
    Retangulo? Sobra = null)
{
    /// <summary>"Sobra 1000 × 397 mm".</summary>
    public string? TextoSobra => Sobra is { } s ? $"Sobra {s.Comprimento:0.#} × {s.Largura:0.#} mm" : null;

    public double AreaPecas => Pecas.Sum(p => p.AreaReal);

    /// <summary>Fração da chapa que vira peça (0 a 1).</summary>
    public double Aproveitamento => AreaChapa > 0 ? AreaPecas / AreaChapa : 0;
}

/// <summary>Resultado do cálculo.</summary>
/// <param name="ChapasComPecasMenores">
/// Quando o plano usa mais de uma chapa e a espessura do corte é o que impede mais peças por chapa:
/// em quantas chapas caberia se cada peça tivesse a espessura a menos no comprimento e na largura.
/// </param>
public sealed record ResultadoPlano(
    TipoCorte Tipo,
    FormatoChapa Chapa,
    double Refilo,
    double Espessura,
    IReadOnlyList<ChapaDoPlano> Chapas,
    IReadOnlyList<(PecaPedida Peca, int QuantidadeQueNaoCoube)> NaoCouberam,
    int? ChapasComPecasMenores = null)
{
    public int TotalPecas => Chapas.Sum(c => c.Pecas.Count);

    public double AproveitamentoMedio =>
        Chapas.Count == 0 ? 0 : Chapas.Sum(c => c.AreaPecas) / Chapas.Sum(c => c.AreaChapa);

    /// <summary>Qual arrumação o cálculo escolheu, em poucas palavras.</summary>
    public string Arrumacao => Tipo == TipoCorte.Serra ? "Cortes retos (serra ou CNC)" : "Encaixe livre (só CNC)";

    /// <summary>Qual arrumação o cálculo escolheu, explicada.</summary>
    public string ArrumacaoExplicada => Tipo == TipoCorte.Serra
        ? "cortes retos de ponta a ponta (dá para fazer na serra ou na CNC)"
        : "encaixe livre, só na CNC (a fresa contorna cada peça; assim coube mais)";

    /// <summary>"Separe 6 chapas de 1000 × 1000 mm antes de começar." (para o aviso de várias chapas).</summary>
    public string SepareAsChapas
    {
        get
        {
            var medidas = $"{Chapa.Limites.Comprimento:0.#} × {Chapa.Limites.Largura:0.#} mm";
            var quais = Chapa.EhRetanguloSimples ? $"de {medidas}" : $"iguais à desenhada ({medidas})";
            return $"Separe {Chapas.Count} chapas {quais} antes de começar.";
        }
    }

    /// <summary>Por que não coube mais: a espessura do corte entre as peças (só quando é isso que pesa).</summary>
    public string? DicaDaEspessura
    {
        get
        {
            if (ChapasComPecasMenores is not { } n)
                return null;
            return $"Por que não coube mais? Entre uma peça e outra, o corte gasta {Espessura:0.#} mm. " +
                   $"Se cada peça tivesse {Espessura:0.#} mm a menos no comprimento e na largura, daria em {n} chapas.";
        }
    }
}
