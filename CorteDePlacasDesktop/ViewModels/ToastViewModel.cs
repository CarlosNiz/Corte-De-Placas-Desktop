using System;
using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

public enum TipoToast
{
    Sucesso,
    Erro,
}

/// <summary>
/// Aviso rápido no canto da tela confirmando (ou não) o que o operador acabou de fazer.
/// Pode ter uma ação, como "Desfazer".
/// </summary>
public partial class ToastViewModel : ViewModelBase
{
    private readonly Action<ToastViewModel> _fechar;
    private readonly Action? _acao;

    public ToastViewModel(string mensagem, TipoToast tipo, string? textoAcao, Action? acao, string? grupo,
        Action<ToastViewModel> fechar)
    {
        Mensagem = mensagem;
        Tipo = tipo;
        TextoAcao = textoAcao;
        Grupo = grupo;
        _acao = acao;
        _fechar = fechar;
    }

    public string Mensagem { get; }

    /// <summary>Um toast novo do mesmo grupo substitui o anterior, em vez de empilhar.</summary>
    public string? Grupo { get; }

    public TipoToast Tipo { get; }

    public bool EhSucesso => Tipo == TipoToast.Sucesso;

    public bool EhErro => Tipo == TipoToast.Erro;

    public string? TextoAcao { get; }

    public bool TemAcao => TextoAcao is not null;

    [RelayCommand]
    private void ExecutarAcao()
    {
        _acao?.Invoke();
        _fechar(this);
    }

    [RelayCommand]
    private void Fechar() => _fechar(this);
}
