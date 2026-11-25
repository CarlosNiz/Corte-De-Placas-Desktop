using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CorteDePlacasDesktop.Models;
using SkiaSharp;

namespace CorteDePlacasDesktop.Impressao;

/// <summary>
/// Gera o plano de corte em PDF (A4 deitado) para imprimir e levar para a máquina:
/// uma página de resumo e uma página por chapa, com o desenho e a ordem dos cortes.
/// </summary>
public static class PdfDoPlano
{
    private const float Largura = 842;   // A4 deitado, em pontos
    private const float Altura = 595;
    private const float Margem = 36;
    private const float LarguraDaLista = 280;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly SKColor CorTexto = new(0x1A, 0x1D, 0x21);
    private static readonly SKColor CorFraca = new(0x5A, 0x63, 0x70);
    private static readonly SKColor CorLinha = new(0xD0, 0xD4, 0xDA);
    private static readonly SKColor CorChapa = new(0xF4, 0xEE, 0xE4);
    private static readonly SKColor CorBordaChapa = new(0x8C, 0x7B, 0x65);
    private static readonly SKColor CorPeca = new(0xFD, 0xEB, 0xDD);
    private static readonly SKColor CorBordaPeca = new(0xC2, 0x41, 0x0C);
    private static readonly SKColor CorCorte = new(0x1D, 0x4E, 0xD8);
    private static readonly SKColor CorPerigo = new(0xB4, 0x23, 0x18);
    private static readonly SKColor CorAviso = new(0xB5, 0x47, 0x08);

    public static void Gerar(ResultadoPlano resultado, string caminho)
    {
        using var arquivo = File.Create(caminho);
        using var documento = SKDocument.CreatePdf(arquivo);
        foreach (var pagina in Paginas(resultado, DateTime.Now))
        {
            var canvas = documento.BeginPage(Largura, Altura);
            pagina(canvas);
            documento.EndPage();
        }
        documento.Close();
    }

    /// <summary>As páginas do plano, cada uma como uma função que desenha num canvas de 842 × 595.</summary>
    public static IReadOnlyList<Action<SKCanvas>> Paginas(ResultadoPlano r, DateTime data)
    {
        var paginas = new List<Action<SKCanvas>>();
        paginas.AddRange(PaginasDeResumo(r, data));
        foreach (var chapa in r.Chapas)
            paginas.AddRange(PaginasDaChapa(r, chapa));
        return paginas;
    }

    // ------------------------------------------------------------------ resumo

    private static IEnumerable<Action<SKCanvas>> PaginasDeResumo(ResultadoPlano r, DateTime data)
    {
        var pecas = r.Chapas.SelectMany(c => c.Pecas.Select(p => (Chapa: c.Numero, Peca: p))).ToList();
        var tipos = pecas
            .GroupBy(x => x.Peca.Numero)
            .Select(g => (
                Numero: g.Key,
                Descricao: g.First().Peca.Descricao,
                Medidas: g.First().Peca.Medidas,
                Quantidade: g.Count(),
                Desenhada: g.First().Peca.Formato is not null,
                Chapas: string.Join(", ", g.Select(x => x.Chapa).Distinct().Order())))
            .OrderBy(t => t.Numero)
            .ToList();

        // Avisos no topo da primeira página (peças de fora, várias chapas) tiram espaço da tabela
        var linhasNaPrimeira = 16 - (r.NaoCouberam.Count > 0 ? 2 : 0) - (r.Chapas.Count > 1 ? 2 : 0)
                               - (r.ChapasComPecasMenores is not null ? 2 : 0);
        const int linhasPorPagina = 26;
        var blocos = new List<List<(int, string, string, int, bool, string)>>();
        blocos.Add(tipos.Take(linhasNaPrimeira).ToList());
        for (var i = linhasNaPrimeira; i < tipos.Count; i += linhasPorPagina)
            blocos.Add(tipos.Skip(i).Take(linhasPorPagina).ToList());

        for (var b = 0; b < blocos.Count; b++)
        {
            var bloco = blocos[b];
            var primeira = b == 0;
            yield return canvas =>
            {
                canvas.Clear(SKColors.White);
                var y = Margem + 20;
                Texto(canvas, primeira ? "Plano de corte" : "Plano de corte — peças (continuação)", Margem, y, 22, true, CorTexto);
                Texto(canvas, data.ToString("dd/MM/yyyy HH:mm", Cultura), Largura - Margem, y, 10, false, CorFraca, SKTextAlign.Right);
                y += 28;

                if (primeira)
                {
                    var formato = r.Chapa.EhRetanguloSimples ? "retângulo" : "formato desenhado";
                    var linhas = new[]
                    {
                        $"Arrumação: {r.ArrumacaoExplicada}",
                        $"Chapa: {Mm(r.Chapa.Limites.Comprimento)} × {Mm(r.Chapa.Limites.Largura)} mm ({formato})",
                        $"Aparar bordas: {Mm(r.Refilo)} mm   ·   Espessura do corte: {Mm(r.Espessura)} mm",
                        $"Chapas necessárias: {r.Chapas.Count}   ·   Peças: {r.TotalPecas}   ·   Aproveitamento médio: {r.AproveitamentoMedio.ToString("P0", Cultura)}",
                    };
                    foreach (var linha in linhas)
                    {
                        Texto(canvas, linha, Margem, y, 12, false, CorTexto);
                        y += 18;
                    }

                    if (r.NaoCouberam.Count > 0)
                    {
                        y += 4;
                        var aviso = "Não couberam: " + string.Join("; ", r.NaoCouberam.Select(n =>
                            $"{n.QuantidadeQueNaoCoube} × {n.Peca.Descricao} ({Mm(n.Peca.Comprimento)} × {Mm(n.Peca.Largura)} mm)"));
                        foreach (var linha in Quebrar(aviso, 12, true, Largura - 2 * Margem))
                        {
                            Texto(canvas, linha, Margem, y, 12, true, CorPerigo);
                            y += 16;
                        }
                    }

                    if (r.Chapas.Count > 1)
                    {
                        y += 4;
                        var aviso = $"Atenção: este plano precisa de {r.Chapas.Count} chapas. {r.SepareAsChapas}";
                        foreach (var linha in Quebrar(aviso, 12, true, Largura - 2 * Margem))
                        {
                            Texto(canvas, linha, Margem, y, 12, true, CorAviso);
                            y += 16;
                        }
                        if (r.DicaDaEspessura is { } dica)
                        {
                            foreach (var linha in Quebrar(dica, 11, false, Largura - 2 * Margem))
                            {
                                Texto(canvas, linha, Margem, y, 11, false, CorFraca);
                                y += 15;
                            }
                        }
                    }
                    y += 14;
                }

                // Tabela de peças
                float[] colunas = [Margem, Margem + 40, Margem + 330, Margem + 470, Margem + 530, Margem + 640];
                string[] titulos = ["Nº", "Descrição", "Medidas (mm)", "Qtd.", "Formato", "Chapas"];
                for (var c = 0; c < titulos.Length; c++)
                    Texto(canvas, titulos[c], colunas[c], y, 10, true, CorFraca);
                y += 6;
                Linha(canvas, Margem, y, Largura - Margem, y, CorLinha, 1);
                y += 15;
                foreach (var (numero, descricao, medidas, quantidade, desenhada, chapas) in bloco)
                {
                    Texto(canvas, numero.ToString(Cultura), colunas[0], y, 11, true, CorBordaPeca);
                    Texto(canvas, Cortar(descricao, 11, false, colunas[2] - colunas[1] - 10), colunas[1], y, 11, false, CorTexto);
                    Texto(canvas, medidas, colunas[2], y, 11, false, CorTexto);
                    Texto(canvas, quantidade.ToString(Cultura), colunas[3], y, 11, false, CorTexto);
                    Texto(canvas, desenhada ? "Desenhada" : "Retângulo", colunas[4], y, 11, false, CorTexto);
                    Texto(canvas, chapas, colunas[5], y, 11, false, CorTexto);
                    y += 6;
                    Linha(canvas, Margem, y, Largura - Margem, y, CorLinha, 0.5f);
                    y += 13;
                }
            };
        }
    }

    // ------------------------------------------------------------------ chapas

    private static IEnumerable<Action<SKCanvas>> PaginasDaChapa(ResultadoPlano r, ChapaDoPlano chapa)
    {
        var serra = r.Tipo == TipoCorte.Serra;
        var itens = serra
            ? chapa.Cortes.Select(c => (Numero: c.Ordem, Texto: TextoCurto(c), Preparo: c.Tipo != TipoDePasso.Corte)).ToList()
            : chapa.Pecas.Select((p, i) => (Numero: i + 1,
                Texto: $"Peça {p.Numero} · {p.Descricao} — {p.Medidas} mm{(p.Girada ? " (girada)" : "")}", Preparo: false)).ToList();
        var tituloLista = serra ? "Ordem dos cortes" : "Ordem das peças";

        // Quanto da lista cabe ao lado do desenho; o resto vai para páginas de continuação
        const float inicioLista = 112;
        const float inicioContinuacao = 92;
        var larguraColuna = (Largura - 2 * Margem - 40) / 3;
        var linhasLado = Distribuir(itens, LarguraDaLista, Altura - Margem - inicioLista);
        var resto = itens.Skip(linhasLado).ToList();

        yield return canvas =>
        {
            canvas.Clear(SKColors.White);
            Cabecalho(canvas, r, chapa, "");
            var area = new SKRect(Margem, 80, Largura - Margem - LarguraDaLista - 20, Altura - Margem - 16);
            DesenharChapa(canvas, r, chapa, area);
            var legenda = serra
                ? "Números laranja: peças   ·   círculos azuis: cortes, na ordem da lista   ·   (g): peça girada"
                : "Números laranja: peças   ·   (g): peça girada";
            Texto(canvas, legenda, Margem, Altura - Margem, 8.5f, false, CorFraca);

            var x = Largura - Margem - LarguraDaLista;
            Texto(canvas, tituloLista, x, 92, 13, true, CorTexto);
            DesenharLista(canvas, itens.Take(linhasLado), x, inicioLista, LarguraDaLista);
        };

        // Continuação da lista em 3 colunas
        var porColuna = new List<List<(int, string, bool)>>();
        var restantes = resto;
        while (restantes.Count > 0)
        {
            var n = Distribuir(restantes, larguraColuna, Altura - Margem - inicioContinuacao);
            porColuna.Add(restantes.Take(n).ToList());
            restantes = restantes.Skip(n).ToList();
        }
        for (var i = 0; i < porColuna.Count; i += 3)
        {
            var colunas = porColuna.Skip(i).Take(3).ToList();
            yield return canvas =>
            {
                canvas.Clear(SKColors.White);
                Cabecalho(canvas, r, chapa, $" — {tituloLista.ToLowerInvariant()} (continuação)");
                for (var c = 0; c < colunas.Count; c++)
                    DesenharLista(canvas, colunas[c], Margem + c * (larguraColuna + 20), inicioContinuacao, larguraColuna);
            };
        }
    }

    private static void Cabecalho(SKCanvas canvas, ResultadoPlano r, ChapaDoPlano chapa, string complemento)
    {
        Texto(canvas, $"Chapa {chapa.Numero} de {r.Chapas.Count}{complemento}", Margem, Margem + 14, 18, true, CorTexto);
        var detalhe = $"{(chapa.Pecas.Count == 1 ? "1 peça" : $"{chapa.Pecas.Count} peças")}   ·   aproveitamento {chapa.Aproveitamento.ToString("P0", Cultura)}   ·   " +
                      $"chapa {Mm(r.Chapa.Limites.Comprimento)} × {Mm(r.Chapa.Limites.Largura)} mm   ·   " +
                      r.Arrumacao;
        Texto(canvas, detalhe, Margem, Margem + 32, 10.5f, false, CorFraca);
        Linha(canvas, Margem, Margem + 40, Largura - Margem, Margem + 40, CorLinha, 1);
    }

    private static void DesenharChapa(SKCanvas canvas, ResultadoPlano r, ChapaDoPlano chapa, SKRect area)
    {
        var limites = r.Chapa.Limites;
        var margemDesenho = 22f;
        var escala = (float)Math.Min((area.Width - 2 * margemDesenho) / limites.Comprimento,
            (area.Height - 2 * margemDesenho) / limites.Largura);
        var ox = area.Left + (area.Width - (float)limites.Comprimento * escala) / 2;
        var oy = area.Top + (area.Height + (float)limites.Largura * escala) / 2;
        SKPoint T(Ponto p) => new(ox + (float)(p.X - limites.X) * escala, oy - (float)(p.Y - limites.Y) * escala);

        using var preenchimento = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var traco = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        // Chapa
        using (var caminho = Caminho(r.Chapa.Regiao, T))
        {
            preenchimento.Color = CorChapa;
            canvas.DrawPath(caminho, preenchimento);
            traco.Color = CorBordaChapa;
            traco.StrokeWidth = 1.2f;
            canvas.DrawPath(caminho, traco);
        }
        if (r.Refilo > 0)
        {
            using var util = Caminho(r.Chapa.AreaUtil(r.Refilo), T);
            using var tracejado = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = CorBordaChapa, StrokeWidth = 0.6f,
                PathEffect = SKPathEffect.CreateDash([3, 2], 0) };
            canvas.DrawPath(util, tracejado);
        }

        // Medidas da chapa
        var cima = T(new Ponto(limites.Centro.X, limites.Topo));
        Texto(canvas, $"{Mm(limites.Comprimento)} mm", cima.X, cima.Y - 6, 9, false, CorFraca, SKTextAlign.Center);
        var esquerda = T(new Ponto(limites.X, limites.Centro.Y));
        canvas.Save();
        canvas.RotateDegrees(-90, esquerda.X - 6, esquerda.Y);
        Texto(canvas, $"{Mm(limites.Largura)} mm", esquerda.X - 6, esquerda.Y, 9, false, CorFraca, SKTextAlign.Center);
        canvas.Restore();

        // Maior sobra: contorno tracejado e a medida
        if (chapa.Sobra is { } sobra && chapa.TextoSobra is { } textoSobra)
        {
            var canto = T(new Ponto(sobra.X, sobra.Topo));
            var outro = T(new Ponto(sobra.Direita, sobra.Y));
            var rectSobra = new SKRect(canto.X + 2, canto.Y + 2, outro.X - 2, outro.Y - 2);
            if (rectSobra.Width > 0 && rectSobra.Height > 0)
            {
                using var tracejadoSobra = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = CorFraca, StrokeWidth = 0.6f,
                    PathEffect = SKPathEffect.CreateDash([4, 3], 0) };
                canvas.DrawRect(rectSobra, tracejadoSobra);
                if (Medir(textoSobra, 8, true) + 6 < rectSobra.Width && rectSobra.Height > 14)
                {
                    Texto(canvas, textoSobra, rectSobra.MidX, rectSobra.MidY + 3, 8, true, CorFraca, SKTextAlign.Center);
                }
                else if (Medir(textoSobra["Sobra ".Length..], 8, true) + 4 < rectSobra.Width && rectSobra.Height > 24)
                {
                    Texto(canvas, "Sobra", rectSobra.MidX, rectSobra.MidY - 2, 8, true, CorFraca, SKTextAlign.Center);
                    Texto(canvas, textoSobra["Sobra ".Length..], rectSobra.MidX, rectSobra.MidY + 8, 8, true, CorFraca, SKTextAlign.Center);
                }
            }
        }

        // Peças
        foreach (var peca in chapa.Pecas)
        {
            var a = peca.Area;
            var rect = new SKRect(T(new Ponto(a.X, a.Topo)).X, T(new Ponto(a.X, a.Topo)).Y,
                T(new Ponto(a.Direita, a.Y)).X, T(new Ponto(a.Direita, a.Y)).Y);
            preenchimento.Color = CorPeca;
            traco.Color = CorBordaPeca;
            traco.StrokeWidth = 1f;
            if (peca.ContornoNaChapa() is { } contorno)
            {
                using var caminho = Caminho(contorno, T);
                canvas.DrawPath(caminho, preenchimento);
                canvas.DrawPath(caminho, traco);
            }
            else
            {
                canvas.DrawRect(rect, preenchimento);
                canvas.DrawRect(rect, traco);
            }

            var tamanhoNumero = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.35f, 6f, 16f);
            var medidas = peca.Medidas + (peca.Girada ? " (g)" : "");
            var larguraMedidas = Medir(medidas, 7, false);
            if (larguraMedidas + 4 < rect.Width && tamanhoNumero + 10 < rect.Height)
            {
                Texto(canvas, $"{peca.Numero}", rect.MidX, rect.MidY, tamanhoNumero, true, CorBordaPeca, SKTextAlign.Center);
                Texto(canvas, medidas, rect.MidX, rect.MidY + 9, 7, false, CorTexto, SKTextAlign.Center);
            }
            else if (Medir($"{peca.Numero}", tamanhoNumero, true) < rect.Width)
            {
                Texto(canvas, $"{peca.Numero}", rect.MidX, rect.MidY + tamanhoNumero / 3, tamanhoNumero, true, CorBordaPeca, SKTextAlign.Center);
            }
        }

        // Cortes (serra): linha tracejada azul com o número do corte
        if (r.Tipo != TipoCorte.Serra)
            return;
        using var linhaCorte = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = CorCorte, StrokeWidth = 0.7f,
            PathEffect = SKPathEffect.CreateDash([4, 2], 0) };
        foreach (var corte in chapa.Cortes)
        {
            var meio = corte.Tipo == TipoDePasso.Corte ? corte.Posicao + r.Espessura / 2 : corte.Posicao;
            var (p1, p2) = corte.Direcao == DirecaoCorte.Vertical
                ? (T(new Ponto(meio, corte.Inicio)), T(new Ponto(meio, corte.Fim)))
                : (T(new Ponto(corte.Inicio, meio)), T(new Ponto(corte.Fim, meio)));
            canvas.DrawLine(p1, p2, linhaCorte);
            var centro = new SKPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
            preenchimento.Color = SKColors.White;
            canvas.DrawCircle(centro, 5.5f, preenchimento);
            traco.Color = CorCorte;
            traco.StrokeWidth = 0.7f;
            canvas.DrawCircle(centro, 5.5f, traco);
            Texto(canvas, $"{corte.Ordem}", centro.X, centro.Y + 2.3f, 6.5f, true, CorCorte, SKTextAlign.Center);
        }
    }

    /// <summary>
    /// Quantos itens cabem na altura disponível (mesma conta usada por <see cref="DesenharLista"/>:
    /// cada item ocupa suas linhas de texto mais um respiro).
    /// </summary>
    private static int Distribuir(IReadOnlyList<(int Numero, string Texto, bool Preparo)> itens, float largura, float altura)
    {
        var y = 0f;
        for (var i = 0; i < itens.Count; i++)
        {
            var h = AlturaDoItem(itens[i].Texto, largura);
            if (y + h > altura)
                return Math.Max(i, 1);
            y += h;
        }
        return itens.Count;
    }

    private static float AlturaDoItem(string texto, float largura) =>
        Quebrar(texto, 9.5f, false, largura - 30).Count * 12.5f + 5;

    /// <summary>Desenha todos os itens recebidos (quem decide quantos cabem é <see cref="Distribuir"/>).</summary>
    private static void DesenharLista(SKCanvas canvas, IEnumerable<(int Numero, string Texto, bool Preparo)> itens,
        float x, float y, float largura)
    {
        using var circulo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var (numero, texto, preparo) in itens)
        {
            var linhas = Quebrar(texto, 9.5f, false, largura - 30);
            circulo.Color = preparo ? CorFraca : CorCorte;
            canvas.DrawCircle(x + 9, y - 3.5f, 8.5f, circulo);
            Texto(canvas, $"{numero}", x + 9, y, 8.5f, true, SKColors.White, SKTextAlign.Center);
            foreach (var linha in linhas)
            {
                Texto(canvas, linha, x + 24, y, 9.5f, false, CorTexto);
                y += 12.5f;
            }
            y += 5;
        }
    }

    /// <summary>Texto curto do corte para o papel.</summary>
    private static string TextoCurto(CorteSerra c) => c.Tipo switch
    {
        TipoDePasso.Corte => c.Direcao == DirecaoCorte.Vertical
            ? $"Pedaço {Mm(c.Pedaco.Comprimento)} × {Mm(c.Pedaco.Largura)}: cortar na vertical a {Mm(c.Distancia)} mm da esquerda"
            : $"Pedaço {Mm(c.Pedaco.Comprimento)} × {Mm(c.Pedaco.Largura)}: cortar na horizontal a {Mm(c.Distancia)} mm de baixo",
        _ => c.Texto,
    };

    // ------------------------------------------------------------------ utilidades

    private static SKPath Caminho(IReadOnlyList<IReadOnlyList<Ponto>> contornos, Func<Ponto, SKPoint> t)
    {
        var caminho = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contorno in contornos)
        {
            if (contorno.Count < 2)
                continue;
            caminho.AddPoly(contorno.Select(t).ToArray(), close: true);
        }
        return caminho;
    }

    private static string Mm(double valor) => valor.ToString("0.#", Cultura);

    // Uma fonte de cada (normal e negrito) para o documento todo: o PDF guarda cada fonte uma vez só.
    private static readonly SKTypeface FonteNormal =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default;

    private static readonly SKTypeface FonteNegrito =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    private static SKFont Fonte(float tamanho, bool negrito) => new(negrito ? FonteNegrito : FonteNormal, tamanho);

    private static float Medir(string texto, float tamanho, bool negrito)
    {
        using var fonte = Fonte(tamanho, negrito);
        return fonte.MeasureText(texto);
    }

    private static void Texto(SKCanvas canvas, string texto, float x, float y, float tamanho, bool negrito, SKColor cor,
        SKTextAlign alinhamento = SKTextAlign.Left)
    {
        using var fonte = Fonte(tamanho, negrito);
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        canvas.DrawText(texto, x, y, alinhamento, fonte, tinta);
    }

    private static void Linha(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor cor, float espessura)
    {
        using var tinta = new SKPaint { Color = cor, StrokeWidth = espessura, IsAntialias = true, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(x1, y1, x2, y2, tinta);
    }

    /// <summary>Quebra o texto em linhas que cabem na largura.</summary>
    private static List<string> Quebrar(string texto, float tamanho, bool negrito, float largura)
    {
        using var fonte = Fonte(tamanho, negrito);
        var linhas = new List<string>();
        var atual = "";
        foreach (var palavra in texto.Split(' '))
        {
            var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
            if (fonte.MeasureText(tentativa) <= largura || atual.Length == 0)
            {
                atual = tentativa;
            }
            else
            {
                linhas.Add(atual);
                atual = palavra;
            }
        }
        if (atual.Length > 0)
            linhas.Add(atual);
        return linhas;
    }

    /// <summary>Corta o texto com "…" se não couber.</summary>
    private static string Cortar(string texto, float tamanho, bool negrito, float largura)
    {
        using var fonte = Fonte(tamanho, negrito);
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 1 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto + "…";
    }
}
