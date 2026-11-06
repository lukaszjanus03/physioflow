using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace PhysioFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;
    protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>Błąd w formacie middleware'u, rozszerzony o stały kod, po którym frontend rozpoznaje przypadek.</summary>
    protected ObjectResult ProblemWithCode(int status, string title, string code, string detail)
    {
        var result = new ObjectResult(new { title, status, code, detail }) { StatusCode = status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
