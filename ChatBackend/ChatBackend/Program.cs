using ChatBackend.Data;
using ChatBackend.Dtos;
using ChatBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// === DATABASE ===
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=chat.db"));

// === HTTP CLIENT ===
builder.Services.AddHttpClient();

// === SWAGGER ===
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ChatBackend API",
        Version = "v1"
    });
});

// === CORS ===
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "https://full-stack-ai-chat-app.vercel.app", // ✅ Vercel domain
            "http://localhost:3000"                      // ✅ Local test
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

// === MIDDLEWARE SIRASI ===
// CORS en üstte olmalı
app.UseCors("AllowFrontend");

app.UseRouting();
app.UseAuthorization();

// === SWAGGER ===
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ChatBackend API v1");
    c.RoutePrefix = "swagger";
});

// === DATABASE MIGRATION ===
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// === TEST ENDPOINT ===
app.MapGet("/", () => "Backend çalışıyor! 🚀");

// === USER ENDPOINT ===
app.MapPost("/users", async (AppDbContext db, User user) =>
{
    try
    {
        bool exists = await db.Users.AnyAsync(u => u.Name == user.Name);
        if (exists)
            return Results.Conflict($"'{user.Name}' kullanıcı adı zaten mevcut.");

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"/users/{user.Id}", user);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Kullanıcı eklenirken hata oluştu: {ex.Message}");
    }
});

app.MapGet("/users", async (AppDbContext db) =>
    await db.Users.ToListAsync());

// === MESSAGE ENDPOINT ===
app.MapPost("/messages", async (AppDbContext db, IHttpClientFactory httpClientFactory, SendMessageDto dto) =>
{
    var user = await db.Users.FindAsync(dto.UserId);
    if (user == null)
        return Results.BadRequest("Kullanıcı bulunamadı.");

    var predictBaseUrl = "https://noir01-emotion-analysis-ai.hf.space/gradio_api/call/predict";
    var client = httpClientFactory.CreateClient();
    var payload = new { data = new object[] { dto.Text } };

    try
    {
        var postResponse = await client.PostAsJsonAsync(predictBaseUrl, payload);
        if (!postResponse.IsSuccessStatusCode)
        {
            var errorText = await postResponse.Content.ReadAsStringAsync();
            return Results.Problem($"AI servisine erişilemedi. Status: {postResponse.StatusCode}, Body: {errorText}");
        }

        var postJson = await postResponse.Content.ReadAsStringAsync();
        string? eventId = null;

        try
        {
            using var postDoc = JsonDocument.Parse(postJson);
            if (postDoc.RootElement.TryGetProperty("event_id", out var evElem))
                eventId = evElem.GetString();
        }
        catch { }

        string sentiment = "unknown";

        if (!string.IsNullOrWhiteSpace(eventId))
        {
            var sseUrl = $"{predictBaseUrl}/{eventId}";
            var sseResponse = await client.GetAsync(sseUrl, HttpCompletionOption.ResponseHeadersRead);

            if (sseResponse.IsSuccessStatusCode)
            {
                var sseText = await sseResponse.Content.ReadAsStringAsync();
                var lines = sseText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (line.StartsWith("data:"))
                    {
                        var dataPart = line.Substring("data:".Length).Trim();
                        if (string.IsNullOrWhiteSpace(dataPart) || dataPart == "null") continue;

                        try
                        {
                            using var dataDoc = JsonDocument.Parse(dataPart);
                            var root = dataDoc.RootElement;
                            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                                sentiment = root[0].GetString() ?? "unknown";
                            else
                                sentiment = dataPart;
                        }
                        catch { sentiment = dataPart; }

                        break;
                    }
                }
            }
        }

        var message = new Message
        {
            Text = dto.Text,
            Sentiment = sentiment,
            UserId = dto.UserId
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        return Results.Ok(message);
    }
    catch (Exception ex)
    {
        return Results.Problem($"AI isteği başarısız oldu: {ex.Message}");
    }
});

app.MapGet("/messages", async (AppDbContext db) =>
    await db.Messages.Include(m => m.User).ToListAsync());

app.Run();

