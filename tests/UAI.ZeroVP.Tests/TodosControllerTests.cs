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

public class TodosControllerTests
{
    private readonly InMemoryTodoRepository _repository =
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private TodosController CreateController() =>
        new(_repository, TimeProvider.System, NullLogger<TodosController>.Instance)
        {
            ProblemDetailsFactory = new TestProblemDetailsFactory()
        };

    private async Task<TodoItem> AddItemAsync(string title, bool isCompleted = false)
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsCompleted = isCompleted,
            CompletedAtUtc = isCompleted ? DateTimeOffset.UtcNow : null
        };
        return await _repository.AddAsync(item);
    }

    [Fact]
    public async Task GetAll_EmptyRepository_ReturnsOkWithEmptyList()
    {
        var result = await CreateController().GetAll(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsOkWithTask()
    {
        var item = await AddItemAsync("Wanted");

        var result = await CreateController().GetById(item.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(ok.Value);
        Assert.Equal("Wanted", dto.Title);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var result = await CreateController().GetById(Guid.NewGuid(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreatedAndStoresTask()
    {
        var request = new CreateTodoItemRequest { Title = "  New task  " };

        var result = await CreateController().Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(created.Value);
        Assert.Equal("New task", dto.Title);
        Assert.Equal(1, await _repository.CountAsync());
    }

    [Fact]
    public async Task Complete_OpenTask_MarksItCompleted()
    {
        var item = await AddItemAsync("Open");

        var result = await CreateController().Complete(item.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(ok.Value);
        Assert.True(dto.IsCompleted);
        Assert.NotNull(dto.CompletedAtUtc);
    }

    [Fact]
    public async Task Delete_ExistingTask_ReturnsNoContentAndRemovesIt()
    {
        var item = await AddItemAsync("To delete");

        var result = await CreateController().Delete(item.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await _repository.CountAsync());
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        var result = await CreateController().Delete(Guid.NewGuid(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    /// <summary>Kontroler ji normalne dostava z DI; v testu zadne DI neni, tak ji dodame sami.</summary>
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