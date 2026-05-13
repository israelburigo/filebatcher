using System.Reflection;
using System.Text.Json.Serialization;
using FileBatcher.Infrastructure;
using FileBatcher.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=filebatcher.db";
    options.UseSqlite(cs);
});

builder.Services.AddScoped<IFileBatcherService, FileBatcherService>();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()?
    .Where(static o => !string.IsNullOrWhiteSpace(o))
    .Select(static o => o.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();

        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins);
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(static origin =>
            {
                if (string.IsNullOrEmpty(origin)) return false;
                try
                {
                    var host = new Uri(origin).Host;
                    return host is "localhost" or "127.0.0.1";
                }
                catch (UriFormatException)
                {
                    return false;
                }
            });
            return;
        }

        policy.SetIsOriginAllowed(static _ => false);
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "FileBatcher API",
        Version = "v1",
        Description = "Importação e processamento de parceiros via CSV (NOME;EMAIL;CPF;TELEFONE)."
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
        c.IncludeXmlComments(xml, includeControllerXmlComments: true);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseSwagger();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "FileBatcher v1"));

app.UseCors("Frontend");

app.MapControllers();

app.Run();
