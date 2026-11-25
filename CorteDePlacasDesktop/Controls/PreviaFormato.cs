using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.Controls;

/// <summary>
/// Desenho pequeno (só leitura) de um formato — a chapa ou uma peça —, já com furos e recortes,
/// ajustado para caber no espaço.
/// </summary>
public class PreviaFormato : Control
{
    public static readonly StyledProperty<FormatoChapa?> FormatoProperty =
        AvaloniaProperty.Register<PreviaFormato, FormatoChapa?>(nameof(Formato));

    /// <summary>Cor de dentro. Se não for definida, usa a cor da chapa.</summary>
    public static readonly StyledProperty<IBrush?> PreenchimentoProperty =
        AvaloniaProperty.Register<PreviaFormato, IBrush?>(nameof(Preenchimento));

    /// <summary>Cor da borda. Se não for definida, usa a cor da borda da chapa.</summary>
    public static readonly StyledProperty<IBrush?> BordaProperty =
        AvaloniaProperty.Register<PreviaFormato, IBrush?>(nameof(Borda));

    /// <summary>Espaço livre em volta do desenho (px).</summary>
    public static readonly StyledProperty<double> MargemProperty =
        AvaloniaProperty.Register<PreviaFormato, double>(nameof(Margem), 6);

    static PreviaFormato()
    {
        AffectsRender<PreviaFormato>(FormatoProperty, PreenchimentoProperty, BordaProperty, MargemProperty);
    }

    public PreviaFormato()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public FormatoChapa? Formato
    {
        get => GetValue(FormatoProperty);
        set => SetValue(FormatoProperty, value);
    }

    public IBrush? Preenchimento
    {
        get => GetValue(PreenchimentoProperty);
        set => SetValue(PreenchimentoProperty, value);
    }

    public IBrush? Borda
    {
        get => GetValue(BordaProperty);
        set => SetValue(BordaProperty, value);
    }

    public double Margem
    {
        get => GetValue(MargemProperty);
        set => SetValue(MargemProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var formato = Formato;
        if (formato is null || formato.Regiao.Count == 0 || formato.Limites.Comprimento <= 0 || formato.Limites.Largura <= 0)
            return;

        var margem = Margem;
        var limites = formato.Limites;
        var escala = Math.Min((Bounds.Width - 2 * margem) / limites.Comprimento, (Bounds.Height - 2 * margem) / limites.Largura);
        if (escala <= 0)
            return;
        var offsetX = (Bounds.Width - limites.Comprimento * escala) / 2;
        var offsetY = (Bounds.Height + limites.Largura * escala) / 2;
        Point ParaTela(Ponto p) => new(offsetX + (p.X - limites.X) * escala, offsetY - (p.Y - limites.Y) * escala);

        var preenchimento = Preenchimento ?? DesenhoFormato.Pincel(this, "SheetBrush", Brushes.Beige);
        var borda = Borda ?? DesenhoFormato.Pincel(this, "SheetBorderBrush", Brushes.Gray);

        var regiao = formato.Regiao.Select(c => (IReadOnlyList<Point>)c.Select(ParaTela).ToList()).ToList();
        ctx.DrawGeometry(preenchimento, new Pen(borda, 1.8, lineJoin: PenLineJoin.Round), DesenhoFormato.Regiao(regiao));
    }
}
