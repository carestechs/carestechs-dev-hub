namespace DevHub.Contracts.ApplicationErrors;

public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors, string detail = "One or more fields failed validation.", string title = "Validation failed")
    : DomainException(title, detail, status: 400, type: "/probs/validation", errors);
