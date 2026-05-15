namespace DevHub.Contracts.ApplicationErrors;

public sealed class NotFoundException(string detail, string title = "Not Found")
    : DomainException(title, detail, status: 404, type: "/probs/not-found");
