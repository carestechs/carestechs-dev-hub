namespace DevHub.Modules.Workspace.Exceptions;

public sealed class GitHubApiException(string message) : Exception(message);
