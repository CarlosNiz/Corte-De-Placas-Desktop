using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CorteDePlacasDesktop.Models;
using CorteDePlacasDesktop.ViewModels;

namespace CorteDePlacasDesktop.Controls;

/// <summary>
/// Área de desenho do formato da chapa: grade com medidas, contorno, cantos que se arrastam
/// com o mouse e áreas com defeito. A lógica (o que muda) fica no <see cref="EditorChapaViewModel"/>;
/// aqui fica só o desenho e a tradução do mouse para milímetros.
/// </summary>
public class EditorChapaCanvas : Control
{
    public static readonly StyledProperty<EditorChapaViewModel?> EditorProperty =
        AvaloniaProperty.Register<EditorChapaCanvas, EditorChapaViewModel?>(nameof(Editor));

    // Espaço em volta do desenho para as medidas da grade (px)
    private const double MargemEsquerda = 56;
    private const double MargemInferior = 36;
    private const double MargemDireita = 28;
    private const double MargemSuperior = 40;

    private const double RaioCanto = 8;          // px
    private const double RaioToque = 15;         // px: distância para "pegar" um canto
    private const double DistanciaBorda = 10;    // px: distância para clicar numa borda
    private const double DistanciaAlinhar = 9;   // px: distância para alinhar com outro canto
    private const double PassoGrade = 10;        // mm: os pontos "grudam" de 10 em 10 mm

    private static readonly double[] PassosGrade = [10, 20, 50, 100, 200, 250, 500, 1000, 2000, 5000];

    // Área do mundo (mm) mostrada e conversão para a tela
    private Retangulo _mundo = new(0, 0, 2750, 1850);
    private double _escala = 0.1;
    private double _origemX;
    private double _origemY;
    private bool _mundoTravado;

    // Estado do mouse
    private CantoViewModel? _cantoArrastado;
    private Ponto? _inicioDefeito;
    private Ponto? _mouse;
    private CantoViewModel? _cantoSobMouse;
    private bool _sobreBorda;
    private bool _sobrePrimeiroCanto;
    private double? _guiaX;
    private double? _guiaY;

    private static Cursor? _cursorMao;
    private static Cursor? _cursorCruz;

    static EditorChapaCanvas()
    {
        FocusableProperty.OverrideDefaultValue<EditorChapaCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<EditorChapaCanvas>(true);
        AffectsRender<EditorChapaCanvas>(EditorProperty);
    }

    public EditorChapaCanvas()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public EditorChapaViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is EditorChapaViewModel antigo)
                antigo.Alterado -= InvalidateVisual;
            if (change.NewValue is EditorChapaViewModel novo)
                novo.Alterado += InvalidateVisual;
        }
    }

    // ---------------------------------------------------------------- conversões

    private Point ParaTela(Ponto p) => new(_origemX + (p.X - _mundo.X) * _escala, _origemY - (p.Y - _mundo.Y) * _escala);

    private Ponto ParaMundo(Point t) => new(_mundo.X + (t.X - _origemX) / _escala, _mundo.Y + (_origemY - t.Y) / _escala);

    private static double Distancia(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void AtualizarTransformacao(EditorChapaViewModel editor)
    {
        // Enquanto o operador arrasta ou desenha, a escala fica parada para o desenho não "fugir" do mouse.
        if (!_mundoTravado && !editor.EmDesenho)
        {
            var pontos = editor.Cantos.Where(c => c.EstaCompleto).Select(c => c.Ponto)
                .Concat(editor.Defeitos.Where(d => d.EstaCompleto)
                    .SelectMany(d => new[] { new Ponto(d.Area.X, d.Area.Y), new Ponto(d.Area.Direita, d.Area.Topo) }))
                .ToList();
            var limites = Geometria.Limites(pontos);
            if (pontos.Count >= 2 && limites.Comprimento > 1 && limites.Largura > 1)
            {
                var folga = Math.Max(100, Math.Max(limites.Comprimento, limites.Largura) * 0.08);
                _mundo = limites.Expandido(folga);
            }
        }

        var larguraUtil = Math.Max(10, Bounds.Width - MargemEsquerda - MargemDireita);
        var alturaUtil = Math.Max(10, Bounds.Height - MargemSuperior - MargemInferior);
        _escala = Math.Min(larguraUtil / _mundo.Comprimento, alturaUtil / _mundo.Largura);
        _origemX = MargemEsquerda + (larguraUtil - _mundo.Comprimento * _escala) / 2;
        _origemY = MargemSuperior + alturaUtil - (alturaUtil - _mundo.Largura * _escala) / 2;
    }

    /// <summary>
    /// Converte a posição do mouse em milímetros, "grudando" na grade de 10 mm e alinhando
    /// com outros cantos próximos (para bordas retas sem esforço).
    /// </summary>
    private Ponto Ajustar(Point tela, CantoViewModel? ignorar, bool mostrarGuias)
    {
        var p = ParaMundo(tela);
        var x = Math.Round(p.X / PassoGrade) * PassoGrade;
        var y = Math.Round(p.Y / PassoGrade) * PassoGrade;
        double melhorX = DistanciaAlinhar, melhorY = DistanciaAlinhar;
        double? guiaX = null, guiaY = null;

        foreach (var canto in Editor?.Cantos ?? [])
        {
            if (canto == ignorar || !canto.EstaCompleto)
                continue;
            var t = ParaTela(canto.Ponto);
            if (Math.Abs(t.X - tela.X) < melhorX)
            {
                melhorX = Math.Abs(t.X - tela.X);
                x = canto.Ponto.X;
                guiaX = x;
            }
            if (Math.Abs(t.Y - tela.Y) < melhorY)
            {
                melhorY = Math.Abs(t.Y - tela.Y);
                y = canto.Ponto.Y;
                guiaY = y;
            }
        }

        _guiaX = mostrarGuias ? guiaX : null;
        _guiaY = mostrarGuias ? guiaY : null;
        return new Ponto(x, y);
    }

    private CantoViewModel? CantoEm(Point tela) =>
        Editor?.Cantos
            .Where(c => c.EstaCompleto)
            .Select(c => (Canto: c, Distancia: Distancia(ParaTela(c.Ponto), tela)))
            .Where(c => c.Distancia <= RaioToque)
            .OrderBy(c => c.Distancia)
            .Select(c => c.Canto)
            .FirstOrDefault();

    /// <summary>Índice da borda sob o mouse (a que liga o canto i ao i+1), ou -1.</summary>
    private int BordaEm(Point tela, out Ponto pontoNaBorda)
    {
        pontoNaBorda = default;
        var cantos = Editor?.Cantos;
        if (cantos is null || cantos.Count < 2 || cantos.Any(c => !c.EstaCompleto))
            return -1;

        var alvo = new Ponto(tela.X, tela.Y);
        var melhor = -1;
        var menor = DistanciaBorda;
        for (var i = 0; i < cantos.Count; i++)
        {
            var a = ParaTela(cantos[i].Ponto);
            var b = ParaTela(cantos[(i + 1) % cantos.Count].Ponto);
            var d = Geometria.DistanciaAoSegmento(alvo, new Ponto(a.X, a.Y), new Ponto(b.X, b.Y), out _);
            if (d < menor)
            {
                menor = d;
                melhor = i;
            }
        }
        if (melhor < 0)
            return -1;

        // Ponto exato na borda; em bordas retas (horizontais/verticais) ele também gruda na grade.
        var pa = cantos[melhor].Ponto;
        var pb = cantos[(melhor + 1) % cantos.Count].Ponto;
        Geometria.DistanciaAoSegmento(ParaMundo(tela), pa, pb, out var naBorda);
        if (Math.Abs(pa.Y - pb.Y) < 0.5)
            naBorda = new Ponto(Math.Round(naBorda.X / PassoGrade) * PassoGrade, pa.Y);
        else if (Math.Abs(pa.X - pb.X) < 0.5)
            naBorda = new Ponto(pa.X, Math.Round(naBorda.Y / PassoGrade) * PassoGrade);
        else
            naBorda = new Ponto(Math.Round(naBorda.X, 1), Math.Round(naBorda.Y, 1));
        pontoNaBorda = naBorda;
        return melhor;
    }

    // ---------------------------------------------------------------- mouse e teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var editor = Editor;
        var ponto = e.GetCurrentPoint(this);
        if (editor is null || !ponto.Properties.IsLeftButtonPressed)
            return;

        Focus();
        var tela = ponto.Position;

        switch (editor.Modo)
        {
            case ModoEditor.Ajustar:
            {
                var canto = CantoEm(tela);
                if (canto is null && BordaEm(tela, out var naBorda) is var borda and >= 0)
                    canto = editor.InserirCanto(borda, naBorda);

                editor.SelecionarCanto(canto);
                if (canto is not null)
                {
                    _cantoArrastado = canto;
                    _mundoTravado = true;
                    e.Pointer.Capture(this);
                }
                break;
            }

            case ModoEditor.Desenhar:
                if (editor.PodeFecharDesenho && Distancia(ParaTela(editor.Cantos[0].Ponto), tela) <= RaioToque)
                    editor.FecharDesenho();
                else
                    editor.AdicionarCantoDesenho(Ajustar(tela, null, mostrarGuias: true));
                break;

            case ModoEditor.Defeito:
                _inicioDefeito = Ajustar(tela, null, mostrarGuias: false);
                _mundoTravado = true;
                e.Pointer.Capture(this);
                break;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var editor = Editor;
        if (editor is null)
            return;

        var tela = e.GetPosition(this);
        if (_cantoArrastado is not null)
        {
            var p = Ajustar(tela, _cantoArrastado, mostrarGuias: true);
            editor.MoverCanto(_cantoArrastado, p);
            _mouse = p;
            Cursor = CursorMao;
        }
        else
        {
            _mouse = Ajustar(tela, null, mostrarGuias: editor.EmDesenho || _inicioDefeito is not null);
            _cantoSobMouse = editor.EmAjuste ? CantoEm(tela) : null;
            _sobreBorda = editor.EmAjuste && _cantoSobMouse is null && BordaEm(tela, out _) >= 0;
            _sobrePrimeiroCanto = editor.PodeFecharDesenho
                                  && Distancia(ParaTela(editor.Cantos[0].Ponto), tela) <= RaioToque;
            Cursor = _cantoSobMouse is not null || _sobrePrimeiroCanto ? CursorMao
                : _sobreBorda || !editor.EmAjuste ? CursorCruz
                : Cursor.Default;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var editor = Editor;
        if (editor is not null && _inicioDefeito is Ponto inicio)
        {
            var fim = Ajustar(e.GetPosition(this), null, mostrarGuias: false);
            var area = Retangulo.EntrePontos(inicio, fim);
            if (area.Comprimento >= 5 && area.Largura >= 5)
                editor.AdicionarDefeito(area);
        }
        TerminarArraste(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _cantoArrastado = null;
        _inicioDefeito = null;
        _mundoTravado = false;
        _guiaX = _guiaY = null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_cantoArrastado is not null || _inicioDefeito is not null)
            return;
        _mouse = null;
        _cantoSobMouse = null;
        _sobreBorda = _sobrePrimeiroCanto = false;
        InvalidateVisual();
    }

    private void TerminarArraste(IPointer pointer)
    {
        _cantoArrastado = null;
        _inicioDefeito = null;
        _mundoTravado = false;
        _guiaX = _guiaY = null;
        pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var editor = Editor;
        if (editor is null)
            return;

        switch (editor.Modo, e.Key)
        {
            case (ModoEditor.Desenhar, Key.Escape):
                editor.CancelarDesenho();
                break;
            case (ModoEditor.Desenhar, Key.Back):
                editor.DesfazerUltimoCantoDesenho();
                break;
            case (ModoEditor.Desenhar, Key.Enter):
                editor.FecharDesenho();
                break;
            case (ModoEditor.Defeito, Key.Escape):
                editor.ModoAjustarCommand.Execute(null);
                break;
            case (ModoEditor.Ajustar, Key.Escape):
                editor.SelecionarCanto(null);
                break;
            case (ModoEditor.Ajustar, Key.Delete or Key.Back):
                editor.RemoverCantoSelecionadoCommand.Execute(null);
                break;
            case (ModoEditor.Ajustar, Key.Left or Key.Right or Key.Up or Key.Down) when editor.CantoSelecionado is { EstaCompleto: true } canto:
            {
                // Setas movem o canto selecionado 1 mm (com Shift, 10 mm)
                var passo = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                var (dx, dy) = e.Key switch
                {
                    Key.Left => (-passo, 0),
                    Key.Right => (passo, 0),
                    Key.Up => (0, passo),
                    _ => (0, -passo),
                };
                editor.MoverCanto(canto, new Ponto(canto.Ponto.X + dx, canto.Ponto.Y + dy));
                break;
            }
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    private static Cursor CursorMao => _cursorMao ??= new Cursor(StandardCursorType.Hand);

    private static Cursor CursorCruz => _cursorCruz ??= new Cursor(StandardCursorType.Cross);

    // ---------------------------------------------------------------- desenho

    public override void Render(DrawingContext ctx)
    {
        var fundo = DesenhoChapa.Pincel(this, "SurfaceBrush", Brushes.White);
        ctx.DrawRectangle(fundo, null, new Rect(Bounds.Size));

        var editor = Editor;
        if (editor is null || Bounds.Width < 50 || Bounds.Height < 50)
            return;

        AtualizarTransformacao(editor);

        var cores = new Cores(this);
        DesenharGrade(ctx, cores);

        var cantos = editor.Cantos.Where(c => c.EstaCompleto).ToList();
        var pontos = cantos.Select(c => ParaTela(c.Ponto)).ToList();

        if (editor.EmDesenho)
        {
            DesenharContornoEmAndamento(ctx, cores, pontos);
        }
        else if (pontos.Count >= 3)
        {
            ctx.DrawGeometry(cores.Chapa, new Pen(cores.BordaChapa, 2.5, lineJoin: PenLineJoin.Round), DesenhoChapa.Poligono(pontos));
        }

        foreach (var defeito in editor.Defeitos.Where(d => d.EstaCompleto))
            DesenharDefeito(ctx, cores, defeito.Area, $"Defeito {defeito.Numero}");

        if (_inicioDefeito is Ponto inicio && _mouse is Ponto fim)
            DesenharDefeito(ctx, cores, Retangulo.EntrePontos(inicio, fim), null);

        if (!editor.EmDesenho && pontos.Count >= 3)
            DesenharMedidasDasBordas(ctx, cores, cantos, pontos);

        DesenharGuias(ctx, cores);
        DesenharCantos(ctx, cores, editor, cantos, pontos);
        DesenharPosicaoDoMouse(ctx, cores);
    }

    private void DesenharGrade(DrawingContext ctx, Cores cores)
    {
        var menor = PassosGrade.FirstOrDefault(p => p * _escala >= 14, PassosGrade[^1]);
        var maior = PassosGrade.FirstOrDefault(p => p >= menor * 4 && p * _escala >= 80, menor * 5);

        var cantoSupEsq = ParaMundo(new Point(MargemEsquerda, MargemSuperior));
        var cantoInfDir = ParaMundo(new Point(Bounds.Width - MargemDireita, Bounds.Height - MargemInferior));
        var area = new Rect(new Point(MargemEsquerda, MargemSuperior),
            new Point(Bounds.Width - MargemDireita, Bounds.Height - MargemInferior));

        var penMenor = new Pen(cores.GradeFraca, 1);
        var penMaior = new Pen(cores.GradeForte, 1);

        using (ctx.PushClip(area))
        {
            for (var x = Math.Ceiling(cantoSupEsq.X / menor) * menor; x <= cantoInfDir.X; x += menor)
            {
                var sx = Math.Round(ParaTela(new Ponto(x, 0)).X) + 0.5;
                ctx.DrawLine(EhMultiplo(x, maior) ? penMaior : penMenor, new Point(sx, area.Top), new Point(sx, area.Bottom));
            }
            for (var y = Math.Ceiling(cantoInfDir.Y / menor) * menor; y <= cantoSupEsq.Y; y += menor)
            {
                var sy = Math.Round(ParaTela(new Ponto(0, y)).Y) + 0.5;
                ctx.DrawLine(EhMultiplo(y, maior) ? penMaior : penMenor, new Point(area.Left, sy), new Point(area.Right, sy));
            }
        }

        // Medidas da grade (em mm) embaixo e à esquerda
        for (var x = Math.Ceiling(cantoSupEsq.X / maior) * maior; x <= cantoInfDir.X; x += maior)
        {
            var ft = DesenhoChapa.Texto($"{SemMenosZero(x):0}", 11, cores.TextoFraco);
            var sx = ParaTela(new Ponto(x, 0)).X;
            ctx.DrawText(ft, new Point(sx - ft.Width / 2, area.Bottom + 8));
        }
        for (var y = Math.Ceiling(cantoInfDir.Y / maior) * maior; y <= cantoSupEsq.Y; y += maior)
        {
            var ft = DesenhoChapa.Texto($"{SemMenosZero(y):0}", 11, cores.TextoFraco);
            var sy = ParaTela(new Ponto(0, y)).Y;
            ctx.DrawText(ft, new Point(area.Left - ft.Width - 8, sy - ft.Height / 2));
        }
        var unidade = DesenhoChapa.Texto("mm", 11, cores.TextoFraco);
        ctx.DrawText(unidade, new Point(area.Left - unidade.Width - 8, area.Bottom + 8));
    }

    private static bool EhMultiplo(double valor, double passo) => Math.Abs(Math.IEEERemainder(valor, passo)) < 1e-6;

    /// <summary>Evita escrever "-0" na grade.</summary>
    private static double SemMenosZero(double valor) => Math.Abs(valor) < 1e-6 ? 0 : valor;

    private void DesenharContornoEmAndamento(DrawingContext ctx, Cores cores, List<Point> pontos)
    {
        if (pontos.Count >= 2)
            ctx.DrawGeometry(null, new Pen(cores.Marca, 2.5, lineJoin: PenLineJoin.Round), DesenhoChapa.Poligono(pontos, fechado: false));

        if (pontos.Count >= 1 && _mouse is Ponto mouse)
        {
            var destino = _sobrePrimeiroCanto ? pontos[0] : ParaTela(mouse);
            ctx.DrawLine(new Pen(cores.Marca, 2, new DashStyle([3, 3], 0)), pontos[^1], destino);
        }
    }

    private void DesenharDefeito(DrawingContext ctx, Cores cores, Retangulo area, string? rotulo)
    {
        var tela = new Rect(ParaTela(new Ponto(area.X, area.Topo)), ParaTela(new Ponto(area.Direita, area.Y)));
        DesenhoChapa.Defeito(ctx, tela, cores.PerigoSuave, cores.Perigo);
        if (rotulo is null)
            return;
        var ft = DesenhoChapa.Texto(rotulo, 12, cores.Perigo, forte: true);
        if (ft.Width + 12 < tela.Width && ft.Height + 6 < tela.Height)
            DesenhoChapa.Etiqueta(ctx, rotulo, tela.Center, cores.Fundo, cores.Perigo, cores.Perigo);
    }

    /// <summary>Comprimento de cada borda, escrito do lado de fora do contorno.</summary>
    private void DesenharMedidasDasBordas(DrawingContext ctx, Cores cores, List<CantoViewModel> cantos, List<Point> pontos)
    {
        var poligonoTela = pontos.Select(p => new Ponto(p.X, p.Y)).ToList();
        for (var i = 0; i < pontos.Count; i++)
        {
            var a = pontos[i];
            var b = pontos[(i + 1) % pontos.Count];
            var comprimentoTela = Distancia(a, b);
            if (comprimentoTela < 44)
                continue;

            var normal = NormalParaFora(poligonoTela, a, b);
            var meio = new Point((a.X + b.X) / 2 + normal.X * 16, (a.Y + b.Y) / 2 + normal.Y * 16);
            var medida = Geometria.Distancia(cantos[i].Ponto, cantos[(i + 1) % cantos.Count].Ponto);
            DesenhoChapa.Etiqueta(ctx, $"{medida:0.#}", meio, cores.Fundo, cores.GradeForte, cores.Texto);
        }
    }

    private static Point NormalParaFora(List<Ponto> poligonoTela, Point a, Point b)
    {
        var comprimento = Distancia(a, b);
        if (comprimento < 1e-6)
            return default;
        var normal = new Point((b.Y - a.Y) / comprimento, -(b.X - a.X) / comprimento);
        var teste = new Ponto((a.X + b.X) / 2 + normal.X * 4, (a.Y + b.Y) / 2 + normal.Y * 4);
        return Geometria.PontoDentro(poligonoTela, teste) ? new Point(-normal.X, -normal.Y) : normal;
    }

    private void DesenharGuias(DrawingContext ctx, Cores cores)
    {
        var pen = new Pen(cores.Marca, 1, new DashStyle([4, 4], 0));
        if (_guiaX is double gx)
        {
            var x = ParaTela(new Ponto(gx, 0)).X;
            ctx.DrawLine(pen, new Point(x, MargemSuperior), new Point(x, Bounds.Height - MargemInferior));
        }
        if (_guiaY is double gy)
        {
            var y = ParaTela(new Ponto(0, gy)).Y;
            ctx.DrawLine(pen, new Point(MargemEsquerda, y), new Point(Bounds.Width - MargemDireita, y));
        }
    }

    private void DesenharCantos(DrawingContext ctx, Cores cores, EditorChapaViewModel editor,
        List<CantoViewModel> cantos, List<Point> pontos)
    {
        var poligonoTela = pontos.Select(p => new Ponto(p.X, p.Y)).ToList();
        var contorno = new Pen(cores.Marca, 2.5);

        for (var i = 0; i < cantos.Count; i++)
        {
            var canto = cantos[i];
            var centro = pontos[i];
            var destacado = canto.Selecionado || canto == _cantoSobMouse || canto == _cantoArrastado
                            || (i == 0 && _sobrePrimeiroCanto);
            var raio = destacado ? RaioCanto + 3 : RaioCanto;

            // No desenho, o primeiro canto ganha um anel: é onde se clica para fechar.
            if (editor.EmDesenho && i == 0 && cantos.Count >= 3)
                ctx.DrawEllipse(null, new Pen(cores.Marca, 2), centro, raio + 6, raio + 6);

            ctx.DrawEllipse(canto.Selecionado ? cores.Marca : cores.Fundo, contorno, centro, raio, raio);

            // Número do canto (o mesmo da tabela), do lado de fora
            if (pontos.Count >= 3 && !editor.EmDesenho)
            {
                var anterior = pontos[(i + pontos.Count - 1) % pontos.Count];
                var proximo = pontos[(i + 1) % pontos.Count];
                var n1 = NormalParaFora(poligonoTela, anterior, centro);
                var n2 = NormalParaFora(poligonoTela, centro, proximo);
                var dir = new Point(n1.X + n2.X, n1.Y + n2.Y);
                var tamanho = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                dir = tamanho < 1e-6 ? n1 : new Point(dir.X / tamanho, dir.Y / tamanho);
                var ft = DesenhoChapa.Texto($"{canto.Numero}", 12, cores.Marca, forte: true);
                var pos = new Point(centro.X + dir.X * 22 - ft.Width / 2, centro.Y + dir.Y * 22 - ft.Height / 2);
                ctx.DrawText(ft, pos);
            }
            else if (editor.EmDesenho)
            {
                var ft = DesenhoChapa.Texto($"{canto.Numero}", 12, cores.Marca, forte: true);
                ctx.DrawText(ft, new Point(centro.X + 12, centro.Y - ft.Height - 6));
            }
        }

        if (editor.EmDesenho && _sobrePrimeiroCanto && pontos.Count > 0)
            DesenhoChapa.Etiqueta(ctx, "Fechar contorno", new Point(pontos[0].X, pontos[0].Y - 34),
                cores.Marca, cores.Marca, cores.Fundo, 13);
    }

    private void DesenharPosicaoDoMouse(DrawingContext ctx, Cores cores)
    {
        if (_mouse is not Ponto p)
            return;
        var texto = $"X {p.X:0.#}   Y {p.Y:0.#} mm";
        var ft = DesenhoChapa.Texto(texto, 13, cores.Texto, forte: true);
        DesenhoChapa.Etiqueta(ctx, texto, new Point(MargemEsquerda + ft.Width / 2 + 8, 18), cores.Fundo, cores.GradeForte, cores.Texto, 13);
    }

    /// <summary>Cores do tema atual, buscadas uma vez por desenho.</summary>
    private readonly struct Cores(Control c)
    {
        public readonly IBrush Fundo = DesenhoChapa.Pincel(c, "SurfaceBrush", Brushes.White);
        public readonly IBrush Chapa = DesenhoChapa.Pincel(c, "SheetBrush", Brushes.Beige);
        public readonly IBrush BordaChapa = DesenhoChapa.Pincel(c, "SheetBorderBrush", Brushes.Gray);
        public readonly IBrush Marca = DesenhoChapa.Pincel(c, "BrandBrush", Brushes.OrangeRed);
        public readonly IBrush Perigo = DesenhoChapa.Pincel(c, "DangerBrush", Brushes.Red);
        public readonly IBrush PerigoSuave = DesenhoChapa.Pincel(c, "DangerSoftBrush", Brushes.MistyRose);
        public readonly IBrush Texto = DesenhoChapa.Pincel(c, "TextPrimaryBrush", Brushes.Black);
        public readonly IBrush TextoFraco = DesenhoChapa.Pincel(c, "TextSecondaryBrush", Brushes.Gray);
        public readonly IBrush GradeFraca = DesenhoChapa.Pincel(c, "GridMinorBrush", Brushes.WhiteSmoke);
        public readonly IBrush GradeForte = DesenhoChapa.Pincel(c, "SurfaceBorderBrush", Brushes.LightGray);
    }
}
