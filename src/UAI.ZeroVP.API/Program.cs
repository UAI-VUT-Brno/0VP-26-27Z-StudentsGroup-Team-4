using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using UAI.ZeroVP.API.HostedServices;
using UAI.ZeroVP.API.Instances;
using UAI.ZeroVP.API.Logging;
using UAI.ZeroVP.Data.DependencyInjection;
using UAI.ZeroVP.Data.InMemory;
using UAI.ZeroVP.Data.Persistence;
using UAI.ZeroVP.DTO;

var builder = WebApplication.CreateBuilder(args);

// --- Identita instance ----------------------------------------------------

// ID instance (Instance__Id) a adresa se čtou z konfigurace: port určuje ASPNETCORE_URLS / --urls,
// takže vedle sebe může běžet víc instancí. Vystavuje je GET /api/info; popis v README.
builder.Services.AddInstanceIdentity();

// --- Logování -------------------------------------------------------------
builder.AddApplicationLogging();

// --- Služby ---------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

// Datová vrstva: paměťové úložiště s JSON persistencí
builder.Services.AddInMemoryTodoData(
    configureTodo: options =>
        builder.Configuration.GetSection(InMemoryTodoOptions.SectionName).Bind(options),
    configurePersistence: options =>
    {
        builder.Configuration.GetSection(JsonPersistenceOptions.SectionName).Bind(options);

        // Při spuštění pod test runnerem (WebApplicationFactory) persistenci na disk vypneme,
        // aby integrační testy API zůstaly plně izolované a nezanechávaly znečištěný soubor na disku.
        // Test, který sdílené úložiště výslovně zapne (JsonPersistence:Shared=true) a dá mu vlastní soubor
        // v dočasné složce, tím opt-inuje – jinak by nešlo ověřit sdílení dat mezi instancemi.
        if (!options.Shared && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) == true))
        {
            options.Enabled = false;
        }
    });

builder.Services.AddHostedService<TodoPersistenceHostedService>();

builder.Services.AddHealthChecks();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "UAI.ZeroVP – ToDo API",
            Version = "v1",
            Description = "Ukázkové API s persistencí do souboru JSON."
        };

        return Task.CompletedTask;
    });

#pragma warning disable CS0618
    options.AddSchemaTransformer((schema, context, cancellationToken) =>
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(CreateTodoItemRequest))
        {
            schema.Example = new JsonObject
            {
                ["title"] = "Napsat Dockerfile",
                ["description"] = "Připravit Dockerfile pro ToDo API.",
                ["priority"] = "High",
                ["userId"] = "anna"
            };
        }
        else if (type == typeof(UpdateTodoItemRequest))
        {
            schema.Example = new JsonObject
            {
                ["title"] = "Napsat Dockerfile",
                ["description"] = "Připravit Dockerfile pro ToDo API.",
                ["priority"] = "Normal",
                ["isCompleted"] = false,
                ["userId"] = "anna"
            };
        }
        else if (type == typeof(CreateWeeklyTodoItemsRequest))
        {
            schema.Example = new JsonObject
            {
                ["title"] = "Weekly status report",
                ["description"] = "Send the status report to the team.",
                ["priority"] = "Normal",
                ["firstDueAtUtc"] = "2026-11-02T09:00:00+00:00",
                ["repeatCount"] = 3
            };
        }
        else if (type == typeof(TodoItemDto))
        {
            schema.Example = new JsonObject
            {
                ["id"] = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                ["title"] = "Napsat Dockerfile",
                ["description"] = "Připravit Dockerfile pro ToDo API.",
                ["priority"] = "High",
                ["isCompleted"] = false,
                ["createdAtUtc"] = "2026-10-07T08:18:26+00:00",
                ["completedAtUtc"] = null,
                ["userId"] = "anna",
                ["dueAtUtc"] = "2026-10-09T09:00:00+00:00"
            };
        }

        return Task.CompletedTask;
    });
#pragma warning restore CS0618
});

var app = builder.Build();

var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment());

app.UseExceptionHandler();
app.UseRequestLogging();
app.UseStatusCodePages();

if (swaggerEnabled)
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "UAI.ZeroVP – ToDo API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "UAI.ZeroVP – ToDo API";
    });
}

app.MapHealthChecks("/health");
app.MapControllers();

app.MapGet("/", () => Results.Redirect(swaggerEnabled ? "/swagger" : "/api/todos"))
   .ExcludeFromDescription();

var lifecycle = app.Services.GetRequiredService<ApplicationLifecycleLogger>();
lifecycle.LogStarting();

var exitCode = 0;

try
{
    await app.StartAsync();
    await app.WaitForShutdownAsync();
}
catch (Exception exception)
{
    lifecycle.LogFailed(exception);
    exitCode = 1;
}

await app.DisposeAsync();

return exitCode;