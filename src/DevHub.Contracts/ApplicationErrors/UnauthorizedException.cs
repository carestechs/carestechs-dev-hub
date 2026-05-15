namespace DevHub.Contracts.ApplicationErrors;

public sealed class UnauthorizedException(string detail = "Authentication required.", string title = "Unauthorized")
    : DomainException(title, detail, status: 401, type: "/probs/unauthorized");
