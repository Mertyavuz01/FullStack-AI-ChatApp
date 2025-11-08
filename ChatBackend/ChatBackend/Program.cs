using ChatBackend.Data;
using ChatBackend.Dtos;
using ChatBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// SQLite veritabanı ayarı
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=chat.db"));

// HttpClient servisi (AI isteği için)
builder.Services.AddHttpClient();

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ChatBackend API",
        Version = "v1"
    });
});

// CORS — sadece Vercel frontend ve yerel testler için
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "https://full-stack-ai-chat-app.vercel.app", // Vercel domainin
            "http://localhost:3000"                      // Local test için
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

// CORS aktif et
app.UseCors("AllowFrontend");

// Swagger her zaman açık
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ChatBackend API v1");
    c.RoutePrefix = "swagger";
});

// Veritabanı migration kontrolü
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Basit test endpoint
app.MapGet("/", () => "Backend çalışıyor! 🚀");


// Kullanıcı ekleme (Güncellenmiş)
app.MapPost("/users", async (AppDbContext db, User user) =>
{
    // 1. Aynı kullanıcı adının olup olmadığını kontrol et
    bool exists = await db.Users.AnyAsync(u => u.Name == user.Name); 
    // Not: Modelinizdeki kullanıcı adı alanının "Name" olduğunu varsaydım.

    if (exists)
    {
        // 2. Eğer varsa, uygun bir hata mesajı döndür
        return Results.Conflict($"'{user.Name}' kullanıcı adı zaten kullanılıyor.");
    }

    // 3. Kullanıcı adı benzersizse ekle
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"/users/{user.Id}", user); // 201 Created döndürmek daha RESTful'dur
});

// Tüm kullanıcıları listeleme
app.MapGet("/users", async (AppDbContext db) =>
    await db.Users.ToListAsync());

// Mesaj gönderme + AI analizi
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

// Tüm mesajları listeleme
app.MapGet("/messages", async (AppDbContext db) =>
    await db.Messages.Include(m => m.User).ToListAsync());

app.Run();
