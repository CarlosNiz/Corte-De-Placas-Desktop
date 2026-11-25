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
/// Área de desenho da placa: grade com medidas, contorno com cantos que se arrastam,
/// e retângulos/círculos que tiram ou somam área. A lógica (o que muda) fica no
/// <see cref="EditorFormatoViewModel"/>; aqui fica só o desenho e a tradução do mouse para milímetros.
/// </summary>
public class EditorFormatoCanvas : Control
{
    public static readonly StyledProperty<EditorFormatoViewModel?> EditorProperty =
        AvaloniaProperty.Register<EditorFormatoCanvas, EditorFormatoViewModel?>(nameof(Editor));

    // Espaço em volta do desenho para as medidas da grade (px)
    private const double MargemEsquerda = 56;
    private const double MargemInferior = 36;
    private const double MargemDireita = 28;
    private const double MargemSuperior = 40;

    private const double RaioCanto = 8;          // px
    private const double RaioToque = 15;         // px: distância para "pegar" um canto
    private const double DistanciaBorda = 10;    // px: distância para clicar numa borda
    private const double DistanciaAlinhar = 8;   // px: distância para "grudar" numa linha de referência
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
    private FormaViewModel? _formaArrastada;
    private FormaChapa _formaNoInicioDoArraste;
    private Ponto _inicioDoArraste;
    private Ponto? _inicioNovaForma;
    private FormaChapa? _formaNova;
    private Ponto? _mouse;
    private CantoViewModel? _cantoSobMouse;
    private FormaViewModel? _formaSobMouse;
    private bool _sobreBorda;
    private bool _sobrePrimeiroCanto;
    private double? _guiaX;
    private double? _guiaY;
    private string? _rotuloGuiaX;
    private string? _rotuloGuiaY;
    private IReadOnlyList<IReadOnlyList<Ponto>>? _regiaoSemFormaArrastada;

    /// <summary>Com Alt apertado, nada gruda nas linhas de referência (só na grade de 10 mm).</summary>
    private bool _semReferencias;

    private static Cursor? _cursorMao;
    private static Cursor? _cursorCruz;
    private static Cursor? _cursorMover;

    static EditorFormatoCanvas()
    {
        FocusableProperty.OverrideDefaultValue<EditorFormatoCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<EditorFormatoCanvas>(true);
        AffectsRender<EditorFormatoCanvas>(EditorProperty);
    }

    public EditorFormatoCanvas()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public EditorFormatoViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is EditorFormatoViewModel antigo)
                antigo.Alterado -= InvalidateVisual;
            if (change.NewValue is EditorFormatoViewModel novo)
                novo.Alterado += InvalidateVisual;
        }
    }

    // ---------------------------------------------------------------- conversões

    private Point ParaTela(Ponto p) => new(_origemX + (p.X - _mundo.X) * _escala, _origemY - (p.Y - _mundo.Y) * _escala);

    private Ponto ParaMundo(Point t) => new(_mundo.X + (t.X - _origemX) / _escala, _mundo.Y + (_origemY - t.Y) / _escala);

    private Rect ParaTela(Retangulo r) => new(ParaTela(new Ponto(r.X, r.Topo)), ParaTela(new Ponto(r.Direita, r.Y)));

    private static double Distancia(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double NaGrade(double valor) => Math.Round(valor / PassoGrade) * PassoGrade;

    private void AtualizarTransformacao(EditorFormatoViewModel editor)
    {
        // Enquanto o operador arrasta ou desenha, a escala fica parada para o desenho não "fugir" do mouse.
        if (!_mundoTravado && !editor.EmDesenho)
        {
            var pontos = editor.Cantos.Where(c => c.EstaCompleto).Select(c => c.Ponto)
                .Concat(editor.Formas.Where(f => f.EstaCompleta)
                    .SelectMany(f => new[] { new Ponto(f.Forma.Limites.X, f.Forma.Limites.Y), new Ponto(f.Forma.Limites.Direita, f.Forma.Limites.Topo) }))
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
    /// Linhas de referência (em mm): os cantos da placa e as bordas e o centro de cada forma.
    /// </summary>
    private (List<double> Xs, List<double> Ys) Referencias(CantoViewModel? semCanto, FormaViewModel? semForma)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var canto in Editor?.Cantos ?? [])
        {
            if (canto == semCanto || !canto.EstaCompleto)
                continue;
            xs.Add(canto.Ponto.X);
            ys.Add(canto.Ponto.Y);
        }
        foreach (var forma in Editor?.Formas ?? [])
        {
            if (forma == semForma || !forma.EstaCompleta)
                continue;
            var l = forma.Forma.Limites;
            xs.AddRange([l.X, l.Centro.X, l.Direita]);
            ys.AddRange([l.Y, l.Centro.Y, l.Topo]);
        }
        return (xs, ys);
    }

    /// <summary>
    /// Para um trecho [inicio, inicio + tamanho] num eixo: quanto deslocar para que o começo, o meio
    /// ou o fim caia exatamente numa linha de referência próxima. Null se nenhuma estiver perto.
    /// </summary>
    private double? Alinhar(double inicio, double tamanho, List<double> referencias, out double? guia) =>
        Alinhar(inicio, tamanho, referencias, [], out guia, out _);

    /// <summary>
    /// Igual ao anterior, mas também tenta pôr o <b>meio</b> do trecho numa linha de centralização
    /// (centro da placa ou meio entre as bordas). Devolve o nome da linha usada, se for de centralização.
    /// </summary>
    private double? Alinhar(double inicio, double tamanho, List<double> referencias,
        List<(double Valor, string Rotulo)> centros, out double? guia, out string? rotulo)
    {
        guia = null;
        rotulo = null;
        double? melhor = null;
        if (_semReferencias)
            return null;
        var tolerancia = DistanciaAlinhar / _escala;
        foreach (var ancora in new[] { inicio, inicio + tamanho / 2, inicio + tamanho })
        {
            foreach (var r in referencias)
            {
                var d = r - ancora;
                if (Math.Abs(d) < tolerancia && (melhor is null || Math.Abs(d) < Math.Abs(melhor.Value)))
                {
                    melhor = d;
                    guia = r;
                }
            }
        }

        var meio = inicio + tamanho / 2;
        foreach (var (valor, nome) in centros)
        {
            var d = valor - meio;
            if (Math.Abs(d) < tolerancia && (melhor is null || Math.Abs(d) < Math.Abs(melhor.Value) - 1e-6))
            {
                melhor = d;
                guia = valor;
                rotulo = nome;
            }
        }
        return melhor;
    }

    /// <summary>
    /// Linhas de centralização para algo centrado em <paramref name="centro"/>: o centro da placa
    /// e o meio entre as bordas da placa naquela altura (horizontal) e naquela posição (vertical).
    /// </summary>
    private (List<(double Valor, string Rotulo)> Xs, List<(double Valor, string Rotulo)> Ys) Centros(
        Ponto centro, IReadOnlyList<IReadOnlyList<Ponto>>? regiao)
    {
        var xs = new List<(double, string)>();
        var ys = new List<(double, string)>();
        if (regiao is null || regiao.Count == 0)
            return (xs, ys);

        var limites = Geometria.Limites(regiao.SelectMany(p => p));
        xs.Add((limites.Centro.X, $"Centro da {Editor?.Nome ?? "chapa"}"));
        ys.Add((limites.Centro.Y, $"Centro da {Editor?.Nome ?? "chapa"}"));

        if (Geometria.PontoDentroRegiao(regiao, centro))
        {
            if (DistanciaAteBorda(regiao, centro, -1, 0) is double esq && DistanciaAteBorda(regiao, centro, 1, 0) is double dir)
                xs.Add((centro.X + (dir - esq) / 2, "Centralizado entre as bordas"));
            if (DistanciaAteBorda(regiao, centro, 0, -1) is double baixo && DistanciaAteBorda(regiao, centro, 0, 1) is double cima)
                ys.Add((centro.Y + (cima - baixo) / 2, "Centralizado entre as bordas"));
        }
        return (xs, ys);
    }

    /// <summary>
    /// Converte a posição do mouse em milímetros, "grudando" na grade de 10 mm e nas linhas de
    /// referência próximas (cantos e formas), para alinhar sem esforço. Com <paramref name="comCentros"/>,
    /// também gruda no centro da placa e no meio entre as bordas (usado ao criar formas).
    /// </summary>
    private Ponto Ajustar(Point tela, CantoViewModel? ignorar, bool mostrarGuias, bool comCentros = false)
    {
        var p = ParaMundo(tela);
        var (xs, ys) = Referencias(ignorar, null);
        var (cxs, cys) = comCentros ? Centros(p, Editor?.Formato?.Regiao) : ([], []);
        var x = Alinhar(p.X, 0, xs, cxs, out var guiaX, out var rotuloX) is double ax ? p.X + ax : NaGrade(p.X);
        var y = Alinhar(p.Y, 0, ys, cys, out var guiaY, out var rotuloY) is double ay ? p.Y + ay : NaGrade(p.Y);
        _guiaX = mostrarGuias ? guiaX : null;
        _guiaY = mostrarGuias ? guiaY : null;
        _rotuloGuiaX = mostrarGuias ? rotuloX : null;
        _rotuloGuiaY = mostrarGuias ? rotuloY : null;
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

    /// <summary>Forma sob o mouse (a de cima, a última criada, tem prioridade).</summary>
    private FormaViewModel? FormaEm(Point tela)
    {
        var p = ParaMundo(tela);
        var tolerancia = 6 / _escala;
        return Editor?.Formas
            .Where(f => f.EstaCompleta)
            .Reverse()
            .FirstOrDefault(f =>
            {
                var forma = f.Forma;
                return forma.Tipo == TipoForma.Circulo
                    ? Geometria.Distancia(p, forma.Centro) <= forma.Diametro / 2 + tolerancia
                    : forma.Limites.Expandido(tolerancia) is var r
                      && p.X >= r.X && p.X <= r.Direita && p.Y >= r.Y && p.Y <= r.Topo;
            });
    }

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
            naBorda = new Ponto(NaGrade(naBorda.X), pa.Y);
        else if (Math.Abs(pa.X - pb.X) < 0.5)
            naBorda = new Ponto(pa.X, NaGrade(naBorda.Y));
        else
            naBorda = new Ponto(Math.Round(naBorda.X, 1), Math.Round(naBorda.Y, 1));
        pontoNaBorda = naBorda;
        return melhor;
    }

    /// <summary>
    /// Forma que está sendo criada com o mouse (do clique até a posição atual).
    /// O retângulo gruda os cantos nas referências; o círculo ajusta o raio para encostar numa referência.
    /// </summary>
    private FormaChapa? CalcularNovaForma(EditorFormatoViewModel editor, Point tela)
    {
        if (_inicioNovaForma is not Ponto inicio)
            return null;

        if (!editor.EmCirculo)
        {
            var fim = Ajustar(tela, null, mostrarGuias: true);
            _mouse = fim;
            return FormaChapa.Retangular(editor.OperacaoNovaForma, Retangulo.EntrePontos(inicio, fim));
        }

        var bruto = ParaMundo(tela);
        _mouse = bruto;
        var raio = Geometria.Distancia(inicio, bruto);
        var raioFinal = NaGrade(2 * raio) / 2;
        _guiaX = _guiaY = null;
        _rotuloGuiaX = _rotuloGuiaY = null;
        if (_semReferencias)
            return FormaChapa.Circular(editor.OperacaoNovaForma, inicio, 2 * raioFinal);

        var tolerancia = DistanciaAlinhar / _escala;
        var (xs, ys) = Referencias(null, null);
        foreach (var rx in xs)
        {
            var r = Math.Abs(rx - inicio.X);
            if (r > 0 && Math.Abs(r - raio) < tolerancia)
            {
                tolerancia = Math.Abs(r - raio);
                raioFinal = r;
                (_guiaX, _guiaY) = (rx, null);
            }
        }
        foreach (var ry in ys)
        {
            var r = Math.Abs(ry - inicio.Y);
            if (r > 0 && Math.Abs(r - raio) < tolerancia)
            {
                tolerancia = Math.Abs(r - raio);
                raioFinal = r;
                (_guiaX, _guiaY) = (null, ry);
            }
        }
        return FormaChapa.Circular(editor.OperacaoNovaForma, inicio, 2 * raioFinal);
    }

    /// <summary>
    /// A placa sem uma das formas (para medir a distância da forma até a borda sem que ela atrapalhe).
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<Ponto>>? RegiaoSem(EditorFormatoViewModel editor, FormaViewModel? sem)
    {
        if (editor.EmDesenho || editor.Cantos.Count < 3 || editor.Cantos.Any(c => !c.EstaCompleto)
            || editor.Formas.Any(f => !f.EstaCompleta))
        {
            return null;
        }
        return new FormatoChapa(editor.Cantos.Select(c => c.Ponto), editor.Formas.Where(f => f != sem).Select(f => f.Forma)).Regiao;
    }

    /// <summary>
    /// Distância (mm) de um ponto até a borda da placa mais próxima numa direção (horizontal ou vertical).
    /// </summary>
    private static double? DistanciaAteBorda(IReadOnlyList<IReadOnlyList<Ponto>> regiao, Ponto origem, int dx, int dy)
    {
        double? menor = null;
        foreach (var contorno in regiao)
        {
            for (var i = 0; i < contorno.Count; i++)
            {
                var a = contorno[i];
                var b = contorno[(i + 1) % contorno.Count];
                double d;
                if (dy == 0)
                {
                    if ((a.Y > origem.Y) == (b.Y > origem.Y))
                        continue;
                    var x = a.X + (origem.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    d = (x - origem.X) * dx;
                }
                else
                {
                    if ((a.X > origem.X) == (b.X > origem.X))
                        continue;
                    var y = a.Y + (origem.X - a.X) * (b.Y - a.Y) / (b.X - a.X);
                    d = (y - origem.Y) * dy;
                }
                if (d >= -0.01 && (menor is null || d < menor))
                    menor = d;
            }
        }
        return menor is double v ? Math.Max(0, v) : null;
    }

    // ---------------------------------------------------------------- mouse e teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _semReferencias = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
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
                // Prioridade: canto, depois forma, depois borda (cria canto novo).
                var canto = CantoEm(tela);
                if (canto is not null)
                {
                    editor.SelecionarCanto(canto);
                    _cantoArrastado = canto;
                }
                else if (FormaEm(tela) is { } forma)
                {
                    editor.SelecionarForma(forma);
                    _formaArrastada = forma;
                    _formaNoInicioDoArraste = forma.Forma;
                    _regiaoSemFormaArrastada = RegiaoSem(editor, forma);
                    _inicioDoArraste = ParaMundo(tela);
                }
                else if (BordaEm(tela, out var naBorda) is var borda and >= 0)
                {
                    _cantoArrastado = editor.InserirCanto(borda, naBorda);
                }
                else
                {
                    editor.LimparSelecao();
                }

                if (_cantoArrastado is not null || _formaArrastada is not null)
                {
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

            case ModoEditor.Retangulo:
            case ModoEditor.Circulo:
                // O centro do círculo pode ser centralizado; o canto do retângulo, não.
                _inicioNovaForma = Ajustar(tela, null, mostrarGuias: true, comCentros: editor.EmCirculo);
                _formaNova = null;
                _mouse = _inicioNovaForma;
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
        _semReferencias = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
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
        else if (_formaArrastada is not null)
        {
            // A forma acompanha o mouse; se a borda ou o centro dela chega perto de uma linha
            // de referência, ela gruda ali (e a linha aparece). Senão, anda de 10 em 10 mm.
            var atual = ParaMundo(tela);
            var brutoDx = atual.X - _inicioDoArraste.X;
            var brutoDy = atual.Y - _inicioDoArraste.Y;
            var bruto = _formaNoInicioDoArraste.Deslocada(brutoDx, brutoDy).Limites;
            var (xs, ys) = Referencias(null, _formaArrastada);
            var (cxs, cys) = Centros(bruto.Centro, _regiaoSemFormaArrastada);

            var dx = Alinhar(bruto.X, bruto.Comprimento, xs, cxs, out _guiaX, out _rotuloGuiaX) is double ax
                ? brutoDx + ax
                : NaGrade(brutoDx);
            var dy = Alinhar(bruto.Y, bruto.Largura, ys, cys, out _guiaY, out _rotuloGuiaY) is double ay
                ? brutoDy + ay
                : NaGrade(brutoDy);
            editor.MoverForma(_formaArrastada, _formaNoInicioDoArraste.Deslocada(dx, dy));
            _mouse = atual;
            Cursor = CursorMover;
        }
        else if (_inicioNovaForma is not null)
        {
            _formaNova = CalcularNovaForma(editor, tela);
            Cursor = CursorCruz;
        }
        else
        {
            _mouse = Ajustar(tela, null, mostrarGuias: editor.EmDesenho);
            _cantoSobMouse = editor.EmAjuste ? CantoEm(tela) : null;
            _formaSobMouse = editor.EmAjuste && _cantoSobMouse is null ? FormaEm(tela) : null;
            _sobreBorda = editor.EmAjuste && _cantoSobMouse is null && _formaSobMouse is null && BordaEm(tela, out _) >= 0;
            _sobrePrimeiroCanto = editor.PodeFecharDesenho
                                  && Distancia(ParaTela(editor.Cantos[0].Ponto), tela) <= RaioToque;
            Cursor = _cantoSobMouse is not null || _sobrePrimeiroCanto ? CursorMao
                : _formaSobMouse is not null ? CursorMover
                : _sobreBorda || !editor.EmAjuste ? CursorCruz
                : Cursor.Default;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _semReferencias = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var editor = Editor;
        if (editor is not null && _inicioNovaForma is not null)
        {
            if (CalcularNovaForma(editor, e.GetPosition(this)) is { } forma
                && forma.Limites.Comprimento >= 5 && forma.Limites.Largura >= 5)
            {
                editor.AdicionarForma(forma);
            }
        }
        TerminarArraste(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        LimparArraste();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_cantoArrastado is not null || _formaArrastada is not null || _inicioNovaForma is not null)
            return;
        _mouse = null;
        _cantoSobMouse = null;
        _formaSobMouse = null;
        _sobreBorda = _sobrePrimeiroCanto = false;
        InvalidateVisual();
    }

    private void TerminarArraste(IPointer pointer)
    {
        LimparArraste();
        pointer.Capture(null);
        InvalidateVisual();
    }

    private void LimparArraste()
    {
        _cantoArrastado = null;
        _formaArrastada = null;
        _inicioNovaForma = null;
        _formaNova = null;
        _regiaoSemFormaArrastada = null;
        _mundoTravado = false;
        _guiaX = _guiaY = null;
        _rotuloGuiaX = _rotuloGuiaY = null;
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
            case (ModoEditor.Retangulo or ModoEditor.Circulo, Key.Escape):
                editor.ModoAjustarCommand.Execute(null);
                break;
            case (ModoEditor.Ajustar, Key.Escape):
                editor.LimparSelecao();
                break;
            case (ModoEditor.Ajustar, Key.Delete or Key.Back):
                editor.RemoverSelecionadoCommand.Execute(null);
                break;
            case (ModoEditor.Ajustar, Key.Left or Key.Right or Key.Up or Key.Down):
            {
                // Setas movem o canto ou a forma selecionada 1 mm (com Shift, 10 mm)
                var passo = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                var (dx, dy) = e.Key switch
                {
                    Key.Left => (-passo, 0),
                    Key.Right => (passo, 0),
                    Key.Up => (0, passo),
                    _ => (0, -passo),
                };
                if (editor.CantoSelecionado is { EstaCompleto: true } canto)
                    editor.MoverCanto(canto, new Ponto(canto.Ponto.X + dx, canto.Ponto.Y + dy));
                else if (editor.FormaSelecionada is { EstaCompleta: true } forma)
                    editor.MoverForma(forma, forma.Forma.Deslocada(dx, dy));
                else
                    return;
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

    private static Cursor CursorMover => _cursorMover ??= new Cursor(StandardCursorType.SizeAll);

    // ---------------------------------------------------------------- desenho

    public override void Render(DrawingContext ctx)
    {
        var fundo = DesenhoFormato.Pincel(this, "SurfaceBrush", Brushes.White);
        ctx.DrawRectangle(fundo, null, new Rect(Bounds.Size));

        var editor = Editor;
        if (editor is null || Bounds.Width < 50 || Bounds.Height < 50)
            return;

        AtualizarTransformacao(editor);

        var cores = new Cores(this);
        DesenharGrade(ctx, cores);

        var cantos = editor.Cantos.Where(c => c.EstaCompleto).ToList();
        var pontos = cantos.Select(c => ParaTela(c.Ponto)).ToList();
        var temFormas = editor.Formas.Count > 0;

        if (editor.EmDesenho)
        {
            DesenharContornoEmAndamento(ctx, cores, pontos);
        }
        else if (editor.Formato is { } formato)
        {
            // A placa de verdade: contorno + formas que somam − formas que tiram
            var regiao = formato.Regiao.Select(c => (IReadOnlyList<Point>)c.Select(ParaTela).ToList()).ToList();
            ctx.DrawGeometry(cores.Chapa, new Pen(cores.BordaChapa, 2.5, lineJoin: PenLineJoin.Round), DesenhoFormato.Regiao(regiao));
            if (temFormas && pontos.Count >= 3)
                ctx.DrawGeometry(null, new Pen(cores.BordaChapa, 1, new DashStyle([5, 4], 0)), DesenhoFormato.Poligono(pontos));
        }
        else if (pontos.Count >= 3)
        {
            ctx.DrawGeometry(cores.Chapa, new Pen(cores.BordaChapa, 2.5, lineJoin: PenLineJoin.Round), DesenhoFormato.Poligono(pontos));
        }

        foreach (var forma in editor.Formas.Where(f => f.EstaCompleta))
            DesenharForma(ctx, cores, forma.Forma, forma.Remove ? "−" : "+", forma.Numero, forma.Selecionada || forma == _formaSobMouse);

        if (_formaNova is { } nova)
            DesenharForma(ctx, cores, nova, null, 0, destacada: true);

        if (!editor.EmDesenho && pontos.Count >= 3)
            DesenharMedidasDasBordas(ctx, cores, cantos, pontos);

        DesenharGuias(ctx, cores);
        DesenharCotasDaForma(ctx, cores, editor);
        DesenharCantos(ctx, cores, editor, cantos, pontos);
        DesenharPosicaoDoMouse(ctx, cores, editor);
    }

    /// <summary>
    /// Para a forma sendo criada, arrastada ou selecionada: linhas azuis com a distância até a
    /// borda da placa mais próxima, na horizontal e na vertical. No círculo, a medida sai do centro
    /// (é assim que se marca um furo).
    /// </summary>
    private void DesenharCotasDaForma(DrawingContext ctx, Cores cores, EditorFormatoViewModel editor)
    {
        FormaChapa forma;
        IReadOnlyList<IReadOnlyList<Ponto>>? regiao;
        if (_formaNova is { } nova)
        {
            forma = nova;
            regiao = editor.Formato?.Regiao;
        }
        else if ((_formaArrastada ?? editor.FormaSelecionada) is { EstaCompleta: true } vm)
        {
            forma = vm.Forma;
            regiao = RegiaoSem(editor, vm);
        }
        else
        {
            return;
        }
        if (regiao is null || regiao.Count == 0)
            return;

        var l = forma.Limites;
        var circulo = forma.Tipo == TipoForma.Circulo;
        var c = l.Centro;

        // Horizontal: para a esquerda e para a direita; fica a mais curta.
        // Se as duas são iguais (forma centralizada), mostra as duas.
        var origemEsq = circulo ? c : new Ponto(l.X, c.Y);
        var origemDir = circulo ? c : new Ponto(l.Direita, c.Y);
        var esq = DistanciaAteBorda(regiao, origemEsq, -1, 0);
        var dir = DistanciaAteBorda(regiao, origemDir, 1, 0);
        var centradaX = esq is double e1 && dir is double d1 && Math.Abs(e1 - d1) < 0.5;
        if (esq is double e && (dir is null || e <= dir || centradaX))
            DesenharCota(ctx, cores, origemEsq, new Ponto(origemEsq.X - e, origemEsq.Y), e);
        if (dir is double d && (esq is null || d < esq || centradaX))
            DesenharCota(ctx, cores, origemDir, new Ponto(origemDir.X + d, origemDir.Y), d);

        // Vertical: para baixo e para cima; mesma regra
        var origemBaixo = circulo ? c : new Ponto(c.X, l.Y);
        var origemCima = circulo ? c : new Ponto(c.X, l.Topo);
        var baixo = DistanciaAteBorda(regiao, origemBaixo, 0, -1);
        var cima = DistanciaAteBorda(regiao, origemCima, 0, 1);
        var centradaY = baixo is double b1 && cima is double t1 && Math.Abs(b1 - t1) < 0.5;
        if (baixo is double b && (cima is null || b <= cima || centradaY))
            DesenharCota(ctx, cores, origemBaixo, new Ponto(origemBaixo.X, origemBaixo.Y - b), b);
        if (cima is double t && (baixo is null || t < baixo || centradaY))
            DesenharCota(ctx, cores, origemCima, new Ponto(origemCima.X, origemCima.Y + t), t);
    }

    /// <summary>Linha de medida (cota) com traços nas pontas e o valor no meio.</summary>
    private void DesenharCota(DrawingContext ctx, Cores cores, Ponto de, Ponto ate, double valor)
    {
        if (valor < 1)
            return;
        var a = ParaTela(de);
        var b = ParaTela(ate);
        if (Distancia(a, b) < 10)
            return;

        var pen = new Pen(cores.Cota, 1.5);
        ctx.DrawLine(pen, a, b);
        var horizontal = Math.Abs(a.Y - b.Y) < 0.5;
        var (tx, ty) = horizontal ? (0.0, 6.0) : (6.0, 0.0);
        ctx.DrawLine(pen, new Point(a.X - tx, a.Y - ty), new Point(a.X + tx, a.Y + ty));
        ctx.DrawLine(pen, new Point(b.X - tx, b.Y - ty), new Point(b.X + tx, b.Y + ty));

        var meio = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        DesenhoFormato.Etiqueta(ctx, $"{valor:0.#}", meio, cores.Fundo, cores.Cota, cores.Cota, 13);
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
            var ft = DesenhoFormato.Texto($"{SemMenosZero(x):0}", 11, cores.TextoFraco);
            var sx = ParaTela(new Ponto(x, 0)).X;
            ctx.DrawText(ft, new Point(sx - ft.Width / 2, area.Bottom + 8));
        }
        for (var y = Math.Ceiling(cantoInfDir.Y / maior) * maior; y <= cantoSupEsq.Y; y += maior)
        {
            var ft = DesenhoFormato.Texto($"{SemMenosZero(y):0}", 11, cores.TextoFraco);
            var sy = ParaTela(new Ponto(0, y)).Y;
            ctx.DrawText(ft, new Point(area.Left - ft.Width - 8, sy - ft.Height / 2));
        }
        var unidade = DesenhoFormato.Texto("mm", 11, cores.TextoFraco);
        ctx.DrawText(unidade, new Point(area.Left - unidade.Width - 8, area.Bottom + 8));
    }

    private static bool EhMultiplo(double valor, double passo) => Math.Abs(Math.IEEERemainder(valor, passo)) < 1e-6;

    /// <summary>Evita escrever "-0" na grade.</summary>
    private static double SemMenosZero(double valor) => Math.Abs(valor) < 1e-6 ? 0 : valor;

    private void DesenharContornoEmAndamento(DrawingContext ctx, Cores cores, List<Point> pontos)
    {
        if (pontos.Count >= 2)
            ctx.DrawGeometry(null, new Pen(cores.Marca, 2.5, lineJoin: PenLineJoin.Round), DesenhoFormato.Poligono(pontos, fechado: false));

        if (pontos.Count >= 1 && _mouse is Ponto mouse)
        {
            var destino = _sobrePrimeiroCanto ? pontos[0] : ParaTela(mouse);
            ctx.DrawLine(new Pen(cores.Marca, 2, new DashStyle([3, 3], 0)), pontos[^1], destino);
        }
    }

    /// <summary>Contorno tracejado da forma: vermelho se tira área, verde se soma.</summary>
    private void DesenharForma(DrawingContext ctx, Cores cores, FormaChapa forma, string? sinal, int numero, bool destacada)
    {
        var cor = forma.Operacao == OperacaoForma.Remover ? cores.Perigo : cores.Sucesso;
        var pen = destacada ? new Pen(cor, 2.5) : new Pen(cor, 1.8, new DashStyle([4, 3], 0));
        var tela = ParaTela(forma.Limites);

        if (forma.Tipo == TipoForma.Circulo)
            ctx.DrawEllipse(destacada ? cores.Destaque : null, pen, tela.Center, tela.Width / 2, tela.Height / 2);
        else
            ctx.DrawRectangle(destacada ? cores.Destaque : null, pen, tela);

        // Medida no meio da forma, quando cabe
        var medida = forma.Tipo == TipoForma.Circulo
            ? $"Ø {forma.Diametro:0.#}"
            : $"{forma.Limites.Comprimento:0.#} × {forma.Limites.Largura:0.#}";
        var rotulo = sinal is null ? medida : $"{sinal}{numero}  {medida}";
        var ft = DesenhoFormato.Texto(rotulo, 12, cor, forte: true);
        if (ft.Width + 16 < tela.Width && ft.Height + 8 < tela.Height)
        {
            DesenhoFormato.Etiqueta(ctx, rotulo, tela.Center, cores.Fundo, cor, cor);
        }
        else if (sinal is not null)
        {
            // Forma pequena: só o número, do lado de fora
            var curto = $"{sinal}{numero}";
            DesenhoFormato.Etiqueta(ctx, curto, new Point(tela.Right + 14, tela.Top - 8), cores.Fundo, cor, cor);
        }
    }

    /// <summary>Comprimento de cada borda do contorno, escrito do lado de fora.</summary>
    private void DesenharMedidasDasBordas(DrawingContext ctx, Cores cores, List<CantoViewModel> cantos, List<Point> pontos)
    {
        var poligonoTela = pontos.Select(p => new Ponto(p.X, p.Y)).ToList();
        for (var i = 0; i < pontos.Count; i++)
        {
            var a = pontos[i];
            var b = pontos[(i + 1) % pontos.Count];
            if (Distancia(a, b) < 44)
                continue;

            var normal = NormalParaFora(poligonoTela, a, b);
            var meio = new Point((a.X + b.X) / 2 + normal.X * 16, (a.Y + b.Y) / 2 + normal.Y * 16);
            var medida = Geometria.Distancia(cantos[i].Ponto, cantos[(i + 1) % cantos.Count].Ponto);
            DesenhoFormato.Etiqueta(ctx, $"{medida:0.#}", meio, cores.Fundo, cores.GradeForte, cores.Texto);
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

    /// <summary>
    /// Linhas de referência ativas. As de centralização são mais fortes (traço-ponto) e levam
    /// uma etiqueta dizendo o que está centralizado.
    /// </summary>
    private void DesenharGuias(DrawingContext ctx, Cores cores)
    {
        var alinhamento = new Pen(cores.Marca, 1, new DashStyle([4, 4], 0));
        var centralizacao = new Pen(cores.Marca, 1.8, new DashStyle([8, 3, 2, 3], 0));

        if (_guiaX is double gx)
        {
            var x = ParaTela(new Ponto(gx, 0)).X;
            ctx.DrawLine(_rotuloGuiaX is null ? alinhamento : centralizacao,
                new Point(x, MargemSuperior), new Point(x, Bounds.Height - MargemInferior));
            if (_rotuloGuiaX is { } rotulo)
                DesenhoFormato.Etiqueta(ctx, rotulo, new Point(x, MargemSuperior + 14), cores.Marca, cores.Marca, cores.Fundo, 13);
        }
        if (_guiaY is double gy)
        {
            var y = ParaTela(new Ponto(0, gy)).Y;
            ctx.DrawLine(_rotuloGuiaY is null ? alinhamento : centralizacao,
                new Point(MargemEsquerda, y), new Point(Bounds.Width - MargemDireita, y));
            if (_rotuloGuiaY is { } rotulo)
            {
                var largura = DesenhoFormato.Texto(rotulo, 13, cores.Fundo, forte: true).Width;
                DesenhoFormato.Etiqueta(ctx, rotulo, new Point(Bounds.Width - MargemDireita - largura / 2 - 10, y),
                    cores.Marca, cores.Marca, cores.Fundo, 13);
            }
        }
    }

    private void DesenharCantos(DrawingContext ctx, Cores cores, EditorFormatoViewModel editor,
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
                var ft = DesenhoFormato.Texto($"{canto.Numero}", 12, cores.Marca, forte: true);
                ctx.DrawText(ft, new Point(centro.X + dir.X * 22 - ft.Width / 2, centro.Y + dir.Y * 22 - ft.Height / 2));
            }
            else if (editor.EmDesenho)
            {
                var ft = DesenhoFormato.Texto($"{canto.Numero}", 12, cores.Marca, forte: true);
                ctx.DrawText(ft, new Point(centro.X + 12, centro.Y - ft.Height - 6));
            }
        }

        if (editor.EmDesenho && _sobrePrimeiroCanto && pontos.Count > 0)
            DesenhoFormato.Etiqueta(ctx, "Fechar contorno", new Point(pontos[0].X, pontos[0].Y - 34),
                cores.Marca, cores.Marca, cores.Fundo, 13);
    }

    private void DesenharPosicaoDoMouse(DrawingContext ctx, Cores cores, EditorFormatoViewModel editor)
    {
        if (_mouse is not Ponto p)
            return;
        var texto = $"X {p.X:0.#}   Y {p.Y:0.#} mm";
        var forma = _formaNova ?? (_formaArrastada is { EstaCompleta: true } arrastada ? arrastada.Forma : null);
        if (forma is { } f)
        {
            texto = f.Tipo == TipoForma.Circulo
                ? $"Centro X {f.Centro.X:0.#}  Y {f.Centro.Y:0.#}   Ø {f.Diametro:0.#} mm"
                : $"X {f.Limites.X:0.#}  Y {f.Limites.Y:0.#}   {f.Limites.Comprimento:0.#} × {f.Limites.Largura:0.#} mm";
        }
        var ft = DesenhoFormato.Texto(texto, 13, cores.Texto, forte: true);
        DesenhoFormato.Etiqueta(ctx, texto, new Point(MargemEsquerda + ft.Width / 2 + 8, 18), cores.Fundo, cores.GradeForte, cores.Texto, 13);
    }

    /// <summary>Cores do tema atual, buscadas uma vez por desenho.</summary>
    private readonly struct Cores(Control c)
    {
        public readonly IBrush Fundo = DesenhoFormato.Pincel(c, "SurfaceBrush", Brushes.White);
        public readonly IBrush Chapa = DesenhoFormato.Pincel(c, "SheetBrush", Brushes.Beige);
        public readonly IBrush BordaChapa = DesenhoFormato.Pincel(c, "SheetBorderBrush", Brushes.Gray);
        public readonly IBrush Marca = DesenhoFormato.Pincel(c, "BrandBrush", Brushes.OrangeRed);
        public readonly IBrush Destaque = DesenhoFormato.Pincel(c, "HighlightBrush", Brushes.Transparent);
        public readonly IBrush Cota = DesenhoFormato.Pincel(c, "MeasureBrush", Brushes.RoyalBlue);
        public readonly IBrush Perigo = DesenhoFormato.Pincel(c, "DangerBrush", Brushes.Red);
        public readonly IBrush Sucesso = DesenhoFormato.Pincel(c, "SuccessBrush", Brushes.Green);
        public readonly IBrush Texto = DesenhoFormato.Pincel(c, "TextPrimaryBrush", Brushes.Black);
        public readonly IBrush TextoFraco = DesenhoFormato.Pincel(c, "TextSecondaryBrush", Brushes.Gray);
        public readonly IBrush GradeFraca = DesenhoFormato.Pincel(c, "GridMinorBrush", Brushes.WhiteSmoke);
        public readonly IBrush GradeForte = DesenhoFormato.Pincel(c, "SurfaceBorderBrush", Brushes.LightGray);
    }
}
