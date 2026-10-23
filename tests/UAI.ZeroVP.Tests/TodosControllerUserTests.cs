using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.API.Controllers;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodosControllerUserTests
{
    private readonly InMemoryTodoRepository _repository =
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private TodosController CreateController() =>
        new(_repository, TimeProvider.System, NullLogger<TodosController>.Instance)
        {
            ProblemDetailsFactory = new TestProblemDetailsFactory()
        };

    private Task<TodoItem> AddItemAsync(string title, string? userId) =>
        _repository.AddAsync(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId
        });

    [Fact]
    public async Task GetAll_WithUserId_ReturnsOnlyThatUsersTasks()
    {
        await AddItemAsync("Anna", "anna");
        await AddItemAsync("Petr", "petr");

        var result = await CreateController().GetAll(null, "anna", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(ok.Value);
        var dto = Assert.Single(list);
        Assert.Equal("anna", dto.UserId);
    }

    [Fact]
    public async Task GetAll_WithoutUserId_ReturnsAllTasks()
    {
        await AddItemAsync("Anna", "anna");
        await AddItemAsync("Petr", "petr");

        var result = await CreateController().GetAll(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Create_WithUserId_ReturnsTaskWithUserId()
    {
        var request = new CreateTodoItemRequest { Title = "New", UserId = "  anna  " };

        var result = await CreateController().Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(created.Value);
        Assert.Equal("anna", dto.UserId);
    }

    [Fact]
    public async Task Update_WithUserId_ChangesUserId()
    {
        var item = await AddItemAsync("Old", "anna");
        var request = new UpdateTodoItemRequest { Title = "Old", UserId = "petr" };

        var result = await CreateController().Update(item.Id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(ok.Value);
        Assert.Equal("petr", dto.UserId);
    }

    /// <summary>Kontroler ji normálně dostává z DI; v testu žádné DI není, tak ji dodáme sami.</summary>
    private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) => new()
        {
            Status = statusCode ?? 500,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null,
            string? detail = null, string? instance = null) => new(modelStateDictionary)
        {
            Status = statusCode ?? 400,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };
    }
}