using DevHub.Modules.Workspace.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DevHub.Modules.Workspace.Controllers;

[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController(IOptions<GitHubOptions> github) : ControllerBase
{
    [HttpGet("github/status")]
    public IActionResult GetGitHubStatus() =>
        Ok(new { configured = github.Value.IsConfigured });
}
