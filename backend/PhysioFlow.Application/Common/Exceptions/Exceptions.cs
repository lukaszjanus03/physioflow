namespace PhysioFlow.Application.Common.Exceptions;

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

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
