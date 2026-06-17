namespace Domain.Exceptions;

public class EntityNotFoundException(string entityName, object key)
    : Exception($"{entityName} with key '{key}' was not found.");
