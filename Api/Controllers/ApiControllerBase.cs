using AlfaHackaton.Controllers.Controllers;
using AlfaHackaton.Result;
using ContextService.Models;
using Microsoft.AspNetCore.Mvc;
namespace ContextService.Controllers;

public abstract class ApiControllerBase : BaseController
{
    protected new IActionResult OkOrProblem<T>(Result<T> result)
    {
        if (result.Error is ApiError error)
        {
            return StatusCode(error.StatusCode, new
            {
                code = error.Code,
                message = error.Title
            });
        }
        return base.OkOrProblem(result);
    }
}
