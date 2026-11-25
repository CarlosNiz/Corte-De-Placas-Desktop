using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CorteDePlacasDesktop.Controls;

/// <summary>Peças de desenho em comum entre o editor e a prévia da chapa.</summary>
internal static class DesenhoChapa
{
    private static readonly Typeface Fonte = new(FontFamily.Default);
    private static readonly Typeface FonteForte = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Busca um pincel nos recursos do tema atual (claro/escuro).</summary>
    public static IBrush Pincel(Control controle, string chave, IBrush padrao) =>
        controle.TryFindResource(chave, controle.ActualThemeVariant, out var valor) && valor is IBrush pincel
            ? pincel
            : padrao;

    public static StreamGeometry Poligono(IReadOnlyList<Point> pontos, bool fechado = true)
    {
        var geometria = new StreamGeometry();
        using var contexto = geometria.Open();
        if (pontos.Count > 0)
        {
            contexto.BeginFigure(pontos[0], fechado);
            for (var i = 1; i < pontos.Count; i++)
                contexto.LineTo(pontos[i]);
            contexto.EndFigure(fechado);
        }
        return geometria;
    }

    /// <summary>Área com defeito: fundo avermelhado, hachurado e contorno tracejado.</summary>
    public static void Defeito(DrawingContext ctx, Rect area, IBrush fundo, IBrush cor, double espessura = 1.5)
    {
        ctx.DrawRectangle(fundo, null, area);
        using (ctx.PushClip(area))
        {
            var hachura = new Pen(cor, 1);
            for (var d = -area.Height; d < area.Width; d += 9)
                ctx.DrawLine(hachura, new Point(area.X + d, area.Bottom), new Point(area.X + d + area.Height, area.Y));
        }
        ctx.DrawRectangle(null, new Pen(cor, espessura, new DashStyle([4, 2], 0)), area);
    }

    public static FormattedText Texto(string texto, double tamanho, IBrush cor, bool forte = false) =>
        new(texto, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, forte ? FonteForte : Fonte, tamanho, cor);

    /// <summary>Texto dentro de uma etiqueta arredondada, centralizada no ponto.</summary>
    public static Rect Etiqueta(DrawingContext ctx, string texto, Point centro, IBrush fundo, IBrush borda, IBrush cor,
        double tamanho = 12)
    {
        var ft = Texto(texto, tamanho, cor, forte: true);
        var caixa = new Rect(centro.X - ft.Width / 2 - 6, centro.Y - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
        ctx.DrawRectangle(fundo, new Pen(borda, 1), caixa, 5, 5);
        ctx.DrawText(ft, new Point(caixa.X + 6, caixa.Y + 2));
        return caixa;
    }
}
