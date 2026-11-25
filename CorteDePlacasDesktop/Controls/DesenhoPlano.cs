using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.Controls;

/// <summary>
/// Desenho de uma chapa do plano: a chapa, o refilo, as peças numeradas com medidas e
/// (na serra) as linhas de corte numeradas. Um corte ou peça pode ficar destacado.
/// </summary>
public class DesenhoPlano : Control
{
    public static readonly StyledProperty<ResultadoPlano?> ResultadoProperty =
        AvaloniaProperty.Register<DesenhoPlano, ResultadoPlano?>(nameof(Resultado));

    public static readonly StyledProperty<ChapaDoPlano?> ChapaProperty =
        AvaloniaProperty.Register<DesenhoPlano, ChapaDoPlano?>(nameof(Chapa));

    public static readonly StyledProperty<CorteSerra?> CorteDestacadoProperty =
        AvaloniaProperty.Register<DesenhoPlano, CorteSerra?>(nameof(CorteDestacado));

    public static readonly StyledProperty<PecaNaChapa?> PecaDestacadaProperty =
        AvaloniaProperty.Register<DesenhoPlano, PecaNaChapa?>(nameof(PecaDestacada));

    private const double Margem = 40;

    static DesenhoPlano()
    {
        AffectsRender<DesenhoPlano>(ResultadoProperty, ChapaProperty, CorteDestacadoProperty, PecaDestacadaProperty);
    }

    public DesenhoPlano()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public ResultadoPlano? Resultado
    {
        get => GetValue(ResultadoProperty);
        set => SetValue(ResultadoProperty, value);
    }

    public ChapaDoPlano? Chapa
    {
        get => GetValue(ChapaProperty);
        set => SetValue(ChapaProperty, value);
    }

    public CorteSerra? CorteDestacado
    {
        get => GetValue(CorteDestacadoProperty);
        set => SetValue(CorteDestacadoProperty, value);
    }

    public PecaNaChapa? PecaDestacada
    {
        get => GetValue(PecaDestacadaProperty);
        set => SetValue(PecaDestacadaProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var resultado = Resultado;
        var chapa = Chapa;
        if (resultado is null || chapa is null)
            return;

        var formato = resultado.Chapa;
        var limites = formato.Limites;
        if (limites.Comprimento <= 0 || limites.Largura <= 0)
            return;

        var escala = Math.Min((Bounds.Width - 2 * Margem) / limites.Comprimento, (Bounds.Height - 2 * Margem) / limites.Largura);
        if (escala <= 0)
            return;
        var origemX = (Bounds.Width - limites.Comprimento * escala) / 2;
        var origemY = (Bounds.Height + limites.Largura * escala) / 2;
        Point T(Ponto p) => new(origemX + (p.X - limites.X) * escala, origemY - (p.Y - limites.Y) * escala);
        Rect R(Retangulo r) => new(T(new Ponto(r.X, r.Topo)), T(new Ponto(r.Direita, r.Y)));

        var cores = new Cores(this);

        // Chapa e linha do refilo
        var regiao = formato.Regiao.Select(c => (IReadOnlyList<Point>)c.Select(T).ToList()).ToList();
        ctx.DrawGeometry(cores.Chapa, new Pen(cores.BordaChapa, 2), DesenhoFormato.Regiao(regiao));
        if (resultado.Refilo > 0)
        {
            var util = formato.AreaUtil(resultado.Refilo).Select(c => (IReadOnlyList<Point>)c.Select(T).ToList()).ToList();
            ctx.DrawGeometry(null, new Pen(cores.BordaChapa, 1, new DashStyle([4, 3], 0)), DesenhoFormato.Regiao(util));
        }

        // Medidas da chapa
        var topo = T(new Ponto(limites.Centro.X, limites.Topo));
        DesenhoFormato.Etiqueta(ctx, $"{limites.Comprimento:0.#} mm", new Point(topo.X, topo.Y - 18), cores.Fundo, cores.BordaChapa, cores.TextoFraco, 12);
        var esquerda = T(new Ponto(limites.X, limites.Centro.Y));
        var textoAltura = $"{limites.Largura:0.#} mm";
        var larguraTexto = DesenhoFormato.Texto(textoAltura, 12, cores.TextoFraco, forte: true).Width;
        DesenhoFormato.Etiqueta(ctx, textoAltura, new Point(esquerda.X - larguraTexto / 2 - 14, esquerda.Y), cores.Fundo, cores.BordaChapa, cores.TextoFraco, 12);

        // Maior sobra: contorno tracejado e a medida (para ver na hora se outra peça caberia ali)
        if (chapa.Sobra is { } sobra && chapa.TextoSobra is { } textoSobra)
        {
            var area = R(sobra).Deflate(3);
            if (area.Width > 0 && area.Height > 0)
            {
                ctx.DrawRectangle(null, new Pen(cores.TextoFraco, 1, new DashStyle([6, 4], 0)), area);
                var linha = DesenhoFormato.Texto(textoSobra, 12, cores.TextoFraco, forte: true);
                var titulo = DesenhoFormato.Texto("Sobra", 12, cores.TextoFraco, forte: true);
                var medidas = DesenhoFormato.Texto(textoSobra["Sobra ".Length..], 12, cores.TextoFraco, forte: true);
                if (linha.Width + 12 < area.Width && linha.Height + 6 < area.Height)
                {
                    ctx.DrawText(linha, new Point(area.Center.X - linha.Width / 2, area.Center.Y - linha.Height / 2));
                }
                else if (medidas.Width + 8 < area.Width && titulo.Height + medidas.Height + 6 < area.Height)
                {
                    var y = area.Center.Y - (titulo.Height + medidas.Height) / 2;
                    ctx.DrawText(titulo, new Point(area.Center.X - titulo.Width / 2, y));
                    ctx.DrawText(medidas, new Point(area.Center.X - medidas.Width / 2, y + titulo.Height));
                }
            }
        }

        // Peças
        foreach (var peca in chapa.Pecas)
            DesenharPeca(ctx, cores, peca, R(peca.Area), peca == PecaDestacada, T);

        // Cortes (serra)
        if (resultado.Tipo == TipoCorte.Serra)
        {
            foreach (var corte in chapa.Cortes)
            {
                if (corte != CorteDestacado)
                    DesenharCorte(ctx, cores, corte, resultado.Espessura, destacado: false, T);
            }
            if (CorteDestacado is { } destacado && chapa.Cortes.Contains(destacado))
                DesenharCorte(ctx, cores, destacado, resultado.Espessura, destacado: true, T);
        }
    }

    private static void DesenharPeca(DrawingContext ctx, Cores cores, PecaNaChapa peca, Rect area, bool destacada,
        Func<Ponto, Point> t)
    {
        var borda = new Pen(cores.Marca, destacada ? 3 : 1.5);
        if (peca.ContornoNaChapa() is { } contorno)
        {
            // Peça desenhada: o lugar dela (tracejado) e o formato real dentro
            ctx.DrawRectangle(null, new Pen(cores.Marca, 1, new DashStyle([3, 3], 0)), area);
            var pontos = contorno.Select(c => (IReadOnlyList<Point>)c.Select(t).ToList()).ToList();
            ctx.DrawGeometry(destacada ? cores.Destaque : cores.Peca, borda, DesenhoFormato.Regiao(pontos));
        }
        else
        {
            ctx.DrawRectangle(destacada ? cores.Destaque : cores.Peca, borda, area);
        }

        // Número grande e medidas, se couber
        var numero = DesenhoFormato.Texto($"{peca.Numero}", Math.Clamp(Math.Min(area.Width, area.Height) * 0.4, 10, 22), cores.Marca, forte: true);
        var medidas = DesenhoFormato.Texto(peca.Medidas + (peca.Girada ? " ↻" : ""), 11, cores.Texto);
        var cabeMedidas = medidas.Width + 6 < area.Width && numero.Height + medidas.Height + 4 < area.Height;
        var cabeNumero = numero.Width + 2 < area.Width && numero.Height < area.Height;
        if (cabeMedidas)
        {
            var y = area.Center.Y - (numero.Height + medidas.Height) / 2;
            ctx.DrawText(numero, new Point(area.Center.X - numero.Width / 2, y));
            ctx.DrawText(medidas, new Point(area.Center.X - medidas.Width / 2, y + numero.Height));
        }
        else if (cabeNumero)
        {
            ctx.DrawText(numero, new Point(area.Center.X - numero.Width / 2, area.Center.Y - numero.Height / 2));
        }
    }

    private static void DesenharCorte(DrawingContext ctx, Cores cores, CorteSerra corte, double espessura, bool destacado,
        Func<Ponto, Point> t)
    {
        // A serra passa logo depois da medida: a linha fica no meio da faixa que ela tira
        var meio = corte.Tipo == TipoDePasso.Corte ? corte.Posicao + espessura / 2 : corte.Posicao;
        var (a, b) = corte.Direcao == DirecaoCorte.Vertical
            ? (t(new Ponto(meio, corte.Inicio)), t(new Ponto(meio, corte.Fim)))
            : (t(new Ponto(corte.Inicio, meio)), t(new Ponto(corte.Fim, meio)));

        var cor = destacado ? cores.Destaque2 : cores.Corte;
        ctx.DrawLine(new Pen(cor, destacado ? 3.5 : 1.2, destacado ? null : new DashStyle([6, 3], 0)), a, b);

        var centro = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var raio = destacado ? 13.0 : 8.5;
        ctx.DrawEllipse(destacado ? cores.Destaque2 : cores.Fundo, new Pen(cor, destacado ? 2 : 1), centro, raio, raio);
        var texto = DesenhoFormato.Texto($"{corte.Ordem}", destacado ? 13 : 9.5, destacado ? cores.Fundo : cor, forte: true);
        ctx.DrawText(texto, new Point(centro.X - texto.Width / 2, centro.Y - texto.Height / 2));
    }

    private readonly struct Cores(Control c)
    {
        public readonly IBrush Fundo = DesenhoFormato.Pincel(c, "SurfaceBrush", Brushes.White);
        public readonly IBrush Chapa = DesenhoFormato.Pincel(c, "SheetBrush", Brushes.Beige);
        public readonly IBrush BordaChapa = DesenhoFormato.Pincel(c, "SheetBorderBrush", Brushes.Gray);
        public readonly IBrush Peca = DesenhoFormato.Pincel(c, "BrandSoftBrush", Brushes.Bisque);
        public readonly IBrush Marca = DesenhoFormato.Pincel(c, "BrandBrush", Brushes.OrangeRed);
        public readonly IBrush Destaque = DesenhoFormato.Pincel(c, "HighlightBrush", Brushes.LightYellow);
        public readonly IBrush Destaque2 = DesenhoFormato.Pincel(c, "BrandBrush", Brushes.OrangeRed);
        public readonly IBrush Corte = DesenhoFormato.Pincel(c, "MeasureBrush", Brushes.RoyalBlue);
        public readonly IBrush Texto = DesenhoFormato.Pincel(c, "TextPrimaryBrush", Brushes.Black);
        public readonly IBrush TextoFraco = DesenhoFormato.Pincel(c, "TextSecondaryBrush", Brushes.Gray);
    }
}
