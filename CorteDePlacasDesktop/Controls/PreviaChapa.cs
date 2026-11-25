using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CorteDePlacasDesktop.Models;

namespace CorteDePlacasDesktop.Controls;

/// <summary>Desenho pequeno (só leitura) da placa, já com furos e recortes.</summary>
public class PreviaChapa : Control
{
    public static readonly StyledProperty<FormatoChapa?> FormatoProperty =
        AvaloniaProperty.Register<PreviaChapa, FormatoChapa?>(nameof(Formato));

    static PreviaChapa()
    {
        AffectsRender<PreviaChapa>(FormatoProperty);
    }

    public PreviaChapa()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public FormatoChapa? Formato
    {
        get => GetValue(FormatoProperty);
        set => SetValue(FormatoProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var formato = Formato;
        if (formato is null || formato.Regiao.Count == 0 || formato.Limites.Comprimento <= 0 || formato.Limites.Largura <= 0)
            return;

        const double margem = 6;
        var limites = formato.Limites;
        var escala = Math.Min((Bounds.Width - 2 * margem) / limites.Comprimento, (Bounds.Height - 2 * margem) / limites.Largura);
        if (escala <= 0)
            return;
        var offsetX = (Bounds.Width - limites.Comprimento * escala) / 2;
        var offsetY = (Bounds.Height + limites.Largura * escala) / 2;
        Point ParaTela(Ponto p) => new(offsetX + (p.X - limites.X) * escala, offsetY - (p.Y - limites.Y) * escala);

        var chapa = DesenhoChapa.Pincel(this, "SheetBrush", Brushes.Beige);
        var borda = DesenhoChapa.Pincel(this, "SheetBorderBrush", Brushes.Gray);

        var regiao = formato.Regiao.Select(c => (IReadOnlyList<Point>)c.Select(ParaTela).ToList()).ToList();
        ctx.DrawGeometry(chapa, new Pen(borda, 2, lineJoin: PenLineJoin.Round), DesenhoChapa.Regiao(regiao));
    }
}
