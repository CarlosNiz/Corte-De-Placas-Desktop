using System;
using System.Collections.Generic;

namespace CorteDePlacasDesktop.Models;

/// <summary>Como escolher o lugar de cada peça entre os espaços livres.</summary>
internal enum RegraDeEncaixe
{
    /// <summary>Onde sobra menos no lado mais curto (encaixe justo).</summary>
    MenorSobraCurta,

    /// <summary>Onde sobra menos no lado mais comprido.</summary>
    MenorSobraLonga,

    /// <summary>No menor espaço onde a peça cabe.</summary>
    MenorArea,

    /// <summary>O mais embaixo e à esquerda possível.</summary>
    BaixoEsquerda,
}

/// <summary>
/// Encaixe livre (para CNC) pelo método dos "retângulos máximos" (MaxRects): guarda todos os
/// maiores retângulos vazios — que podem se sobrepor — e coloca cada peça no melhor deles.
/// </summary>
internal sealed class EncaixeLivre
{
    private const double Eps = 1e-6;
    private readonly List<Retangulo> _livres = new();

    /// <param name="area">Retângulo total disponível.</param>
    /// <param name="ocupados">Partes que não podem ser usadas (fora da chapa, furos...).</param>
    public EncaixeLivre(Retangulo area, IEnumerable<Retangulo> ocupados)
    {
        _livres.Add(area);
        foreach (var o in ocupados)
            Ocupar(o);
    }

    /// <summary>Espaços vazios atuais (retângulos máximos).</summary>
    public IReadOnlyList<Retangulo> Livres => _livres;

    /// <summary>
    /// Procura onde colocar um retângulo comprimento × largura (pode girar). Retorna a posição, se girou,
    /// e uma nota (menor é melhor) para comparar com outras peças.
    /// </summary>
    public (Retangulo Posicao, bool Girada, double Nota1, double Nota2)? Procurar(double comprimento, double largura, RegraDeEncaixe regra)
    {
        (Retangulo, bool, double, double)? melhor = null;
        foreach (var livre in _livres)
        {
            Testar(livre, comprimento, largura, false);
            if (Math.Abs(comprimento - largura) > Eps)
                Testar(livre, largura, comprimento, true);
        }
        return melhor;

        void Testar(Retangulo livre, double w, double h, bool girada)
        {
            if (w > livre.Comprimento + Eps || h > livre.Largura + Eps)
                return;
            var sobraX = livre.Comprimento - w;
            var sobraY = livre.Largura - h;
            var (n1, n2) = regra switch
            {
                RegraDeEncaixe.MenorSobraCurta => (Math.Min(sobraX, sobraY), Math.Max(sobraX, sobraY)),
                RegraDeEncaixe.MenorSobraLonga => (Math.Max(sobraX, sobraY), Math.Min(sobraX, sobraY)),
                RegraDeEncaixe.MenorArea => (livre.Area - w * h, Math.Min(sobraX, sobraY)),
                _ => (livre.Y + h, livre.X),
            };
            if (melhor is null || n1 < melhor.Value.Item3 - Eps
                || (Math.Abs(n1 - melhor.Value.Item3) <= Eps && n2 < melhor.Value.Item4))
            {
                melhor = (new Retangulo(livre.X, livre.Y, w, h), girada, n1, n2);
            }
        }
    }

    /// <summary>Marca um retângulo como ocupado e recalcula os espaços vazios máximos.</summary>
    public void Ocupar(Retangulo usado)
    {
        var novos = new List<Retangulo>();
        for (var i = _livres.Count - 1; i >= 0; i--)
        {
            var livre = _livres[i];
            if (!livre.Sobrepoe(usado))
                continue;
            _livres.RemoveAt(i);

            // O que sobra do espaço livre em volta do usado: até 4 retângulos (esquerda, direita, baixo, cima)
            if (usado.X > livre.X + Eps)
                novos.Add(new Retangulo(livre.X, livre.Y, usado.X - livre.X, livre.Largura));
            if (usado.Direita < livre.Direita - Eps)
                novos.Add(new Retangulo(usado.Direita, livre.Y, livre.Direita - usado.Direita, livre.Largura));
            if (usado.Y > livre.Y + Eps)
                novos.Add(new Retangulo(livre.X, livre.Y, livre.Comprimento, usado.Y - livre.Y));
            if (usado.Topo < livre.Topo - Eps)
                novos.Add(new Retangulo(livre.X, usado.Topo, livre.Comprimento, livre.Topo - usado.Topo));
        }
        _livres.AddRange(novos);
        RemoverContidos();
    }

    private void RemoverContidos()
    {
        for (var i = 0; i < _livres.Count; i++)
        {
            for (var j = i + 1; j < _livres.Count; j++)
            {
                if (Contem(_livres[j], _livres[i]))
                {
                    _livres.RemoveAt(i);
                    i--;
                    break;
                }
                if (Contem(_livres[i], _livres[j]))
                {
                    _livres.RemoveAt(j);
                    j--;
                }
            }
        }
    }

    private static bool Contem(Retangulo fora, Retangulo dentro) =>
        dentro.X >= fora.X - Eps && dentro.Y >= fora.Y - Eps
        && dentro.Direita <= fora.Direita + Eps && dentro.Topo <= fora.Topo + Eps;
}
