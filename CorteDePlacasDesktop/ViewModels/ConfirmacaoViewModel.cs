using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace CorteDePlacasDesktop.ViewModels;

/// <summary>
/// Diálogo que pergunta ao operador se ele quer mesmo fazer algo que não tem volta.
/// </summary>
public partial class ConfirmacaoViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _resposta = new();

    public ConfirmacaoViewModel(string titulo, string mensagem, string textoConfirmar, string textoCancelar)
    {
        Titulo = titulo;
        Mensagem = mensagem;
        TextoConfirmar = textoConfirmar;
        TextoCancelar = textoCancelar;
    }

    public string Titulo { get; }

    public string Mensagem { get; }

    public string TextoConfirmar { get; }

    public string TextoCancelar { get; }

    /// <summary>Termina quando o operador responde: true se confirmou.</summary>
    public Task<bool> Resposta => _resposta.Task;

    [RelayCommand]
    private void Confirmar() => _resposta.TrySetResult(true);

    [RelayCommand]
    private void Cancelar() => _resposta.TrySetResult(false);
}
