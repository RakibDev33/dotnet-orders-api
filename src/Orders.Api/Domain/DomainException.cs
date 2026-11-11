namespace Orders.Api.Domain;

/// <summary>Raised when an operation would violate a business rule of the order aggregate.</summary>
public sealed class DomainException(string message) : Exception(message);
