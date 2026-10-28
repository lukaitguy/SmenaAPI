using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Employees;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

[ApiController]
[Authorize(Policy = Policies.Manager)]
[Route("api/employees")]
public sealed class EmployeesController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EmployeeDto>> List(ListEmployeesHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Get(Guid id, GetEmployeeHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Update(
        Guid id, UpdateEmployeeCommand command, UpdateEmployeeHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<EmployeeDto>> SetStatus(
        Guid id, SetEmployeeStatusCommand command, SetEmployeeStatusHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }
}
