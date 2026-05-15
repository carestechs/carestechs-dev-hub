namespace DevHub.Contracts.ApplicationErrors;

public sealed class ConflictException(string detail, string title = "Conflict")
    : DomainException(title, detail, status: 409, type: "/probs/conflict");
