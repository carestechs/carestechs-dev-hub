namespace DevHub.Contracts.ApplicationErrors;

public sealed class ForbiddenException(string detail, string title = "Forbidden")
    : DomainException(title, detail, status: 403, type: "/probs/forbidden");
