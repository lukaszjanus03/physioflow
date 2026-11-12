namespace PhysioFlow.Application.Common.Exceptions;

/// <summary>Provider diagnostics remain internal; API responses never include storage credentials.</summary>
public class StorageUnavailableException : Exception
{
    public StorageUnavailableException(Exception? inner = null)
        : base("Przechowywanie zdjęć jest chwilowo niedostępne.", inner) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string name, object key)
        : base($"Entity \"{name}\" ({key}) was not found.") { }
}

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException() : base("Access to this resource is forbidden.") { }
    public ForbiddenAccessException(string message) : base(message) { }
}

/// <summary>Message trafia do odpowiedzi i logów, więc nie może zawierać danych osobowych ani medycznych.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
    public ConflictException(string code, string message) : base(message) => Code = code;

    /// <summary>Kod błędu dla frontendu (pole "code" w problem+json).</summary>
    public string? Code { get; }
}

public class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

/// <summary>
/// Dostawca poczty odrzucił wiadomość albo jest niedostępny.
/// Message trafia do odpowiedzi HTTP i logów, więc nie może zawierać linków ani tokenów.
/// </summary>
public class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message) : base(message) { }
    public EmailDeliveryException(string message, Exception inner) : base(message, inner) { }
}
