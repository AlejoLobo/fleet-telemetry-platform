using FleetTelemetry.Api.Controllers;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Application.Tests.TestHelpers;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class FleetPaginationEndpointTests
{
    [Fact]
    public async Task Cursor_invalido_retorna_400()
    {
        var controller = CreateController(new TestHelpers.FakeFleetQueryService([]));

        var result = await controller.GetAll(pageSize: null, cursor: "%%%invalid%%%", cancellationToken: CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task pageSize_superior_al_maximo_retorna_400()
    {
        var controller = CreateController(new TestHelpers.FakeFleetQueryService([]));

        var result = await controller.GetAll(pageSize: 501, cursor: null, cancellationToken: CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    private static FleetController CreateController(IFleetQueryService fleetQueryService)
    {
        var options = Options.Create(new QueryLimitsOptions());
        return new FleetController(fleetQueryService, options);
    }
}
