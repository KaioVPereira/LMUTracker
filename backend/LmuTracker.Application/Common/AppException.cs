namespace LmuTracker.Application.Common;

/// <summary>Erro de negócio com código HTTP — vira resposta limpa para o cliente (ver middleware na Api).</summary>
public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
