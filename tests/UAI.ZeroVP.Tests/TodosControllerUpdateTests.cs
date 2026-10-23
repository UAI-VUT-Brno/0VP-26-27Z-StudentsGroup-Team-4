using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using UAI.ZeroVP.API.Controllers;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests;

public class TodosControllerUpdateTests
{
    private readonly InMemoryTodoRepository _repository =
        new(new InMemoryTodoOptions { SeedSampleData = false });

    private TodosController CreateController(ITodoRepository? repository = null) =>
        new(repository ?? _repository, TimeProvider.System, NullLogger<TodosController>.Instance)
        {
            ProblemDetailsFactory = new TestProblemDetailsFactory()
        };

    private static TodoItem CreateItem(string title, bool isCompleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        IsCompleted = isCompleted,
        CompletedAtUtc = isCompleted ? DateTimeOffset.UtcNow : null
    };

    [Fact]
    public async Task Update_ExistingTask_ReturnsOkWithChangedValues()
    {
        var item = await _repository.AddAsync(CreateItem("Old"));
        var request = new UpdateTodoItemRequest
        {
            Title = "  New  ",
            Description = "Details",
            Priority = TodoPriority.High,
            IsCompleted = true
        };

        var result = await CreateController().Update(item.Id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TodoItemDto>(ok.Value);
        Assert.Equal("New", dto.Title);
        Assert.Equal("Details", dto.Description);
        Assert.Equal(TodoPriority.High, dto.Priority);
        Assert.True(dto.IsCompleted);
        Assert.NotNull(dto.CompletedAtUtc);
    }

    [Fact]
    public async Task Update_ExistingTask_PersistsChangeInRepository()
    {
        var item = await _repository.AddAsync(CreateItem("Old"));

        await CreateController().Update(item.Id, new UpdateTodoItemRequest { Title = "New" }, CancellationToken.None);

        var stored = await _repository.GetByIdAsync(item.Id);
        Assert.Equal("New", stored!.Title);
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        var result = await CreateController().Update(
            Guid.NewGuid(), new UpdateTodoItemRequest { Title = "A" }, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task Update_TaskDisappearsBeforeSave_Returns404()
    {
        var item = CreateItem("Vanishing");
        var controller = CreateController(new VanishingRepository(item));

        var result = await controller.Update(item.Id, new UpdateTodoItemRequest { Title = "A" }, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task Complete_UnknownId_Returns404()
    {
        var result = await CreateController().Complete(Guid.NewGuid(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task GetAll_WithCompletionFilter_ReturnsOnlyMatchingTasks(bool filter, int expectedCount)
    {
        await _repository.AddAsync(CreateItem("Done", isCompleted: true));
        await _repository.AddAsync(CreateItem("Open 1"));
        await _repository.AddAsync(CreateItem("Open 2"));

        var result = await CreateController().GetAll(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<TodoItemDto>>(ok.Value);
        Assert.Equal(expectedCount, list.Count);
    }

    /// <summary>Repozitar, ktery ukol najde, ale ulozeni selze (jako by ho mezitim nekdo smazal).</summary>
    private sealed class VanishingRepository : ITodoRepository
    {
        private readonly TodoItem _item;

        public VanishingRepository(TodoItem item) => _item = item;

        public Task<IReadOnlyList<TodoItem>> GetAllAsync(bool? isCompleted = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TodoItem>>(new[] { _item });

        public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<TodoItem?>(_item.Clone());

        public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default) =>
            Task.FromResult(item);

        public Task<IReadOnlyList<TodoItem>> AddRangeAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TodoItem>>(items.ToList());

        public Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
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