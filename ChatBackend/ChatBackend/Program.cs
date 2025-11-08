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

// CORS (frontend için serbest)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Gerekirse migrationları otomatik uygula (tablo hatalarını azaltmak için)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Swagger her zaman açık
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ChatBackend API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAll");

// Basit test endpoint
app.MapGet("/", () => "Backend çalışıyor! 🚀");

// Kullanıcı ekleme
app.MapPost("/users", async (AppDbContext db, User user) =>
{
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(user);
});

// Tüm kullanıcıları listeleme
app.MapGet("/users", async (AppDbContext db) =>
    await db.Users.ToListAsync());

// Mesaj gönderme + AI analizi
app.MapPost("/messages", async (AppDbContext db, IHttpClientFactory httpClientFactory, SendMessageDto dto) =>
{
    // Kullanıcı kontrolü
    var user = await db.Users.FindAsync(dto.UserId);
    if (user == null)
        return Results.BadRequest("Kullanıcı bulunamadı.");

    // Hugging Face Space base URL (queue'lu API)
    var predictBaseUrl = "https://noir01-emotion-analysis-ai.hf.space/gradio_api/call/predict";

    var client = httpClientFactory.CreateClient();

    // 1) İlk istek: event_id almak için
    var payload = new
    {
        data = new object[] { dto.Text }
    };

    try
    {
        // 1. ADIM: POST → event_id al
        var postResponse = await client.PostAsJsonAsync(predictBaseUrl, payload);
        if (!postResponse.IsSuccessStatusCode)
        {
            var errorText = await postResponse.Content.ReadAsStringAsync();
            return Results.Problem($"AI servisine erişilemedi. Status: {postResponse.StatusCode}, Body: {errorText}");
        }

        var postJson = await postResponse.Content.ReadAsStringAsync();
        Console.WriteLine("HF POST JSON: " + postJson);

        string? eventId = null;
        try
        {
            using var postDoc = JsonDocument.Parse(postJson);
            if (postDoc.RootElement.TryGetProperty("event_id", out var evElem))
            {
                eventId = evElem.GetString();
            }
        }
        catch
        {
            // parse hatası olursa eventId null kalır
        }

        string sentiment = "unknown";

        if (!string.IsNullOrWhiteSpace(eventId))
        {
            // 2. ADIM: GET → SSE stream içinden sonucu al
            var sseUrl = $"{predictBaseUrl}/{eventId}";

            var sseResponse = await client.GetAsync(sseUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!sseResponse.IsSuccessStatusCode)
            {
                var err = await sseResponse.Content.ReadAsStringAsync();
                sentiment = $"ai_error: {sseResponse.StatusCode} - {err}";
            }
            else
            {
                var sseText = await sseResponse.Content.ReadAsStringAsync();
                Console.WriteLine("HF SSE RAW: " + sseText);

                // SSE formatında satır satır geliyor → "data: ... "
                var lines = sseText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (line.StartsWith("data:"))
                    {
                        var dataPart = line.Substring("data:".Length).Trim();
                        if (string.IsNullOrWhiteSpace(dataPart) || dataPart == "null")
                            continue;

                        try
                        {
                            // dataPart genelde JSON array oluyor: ["POSITIVE", 0.98, ...]
                            using var dataDoc = JsonDocument.Parse(dataPart);
                            var root = dataDoc.RootElement;

                            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                            {
                                var first = root[0];
                                if (first.ValueKind == JsonValueKind.String)
                                    sentiment = first.GetString() ?? "unknown";
                                else
                                    sentiment = first.ToString();
                            }
                            else
                            {
                                sentiment = dataPart;
                            }
                        }
                        catch
                        {
                            sentiment = dataPart;
                        }

                        break;
                    }
                }

                // Hiçbir şey bulunamazsa komple SSE'yi kaydedelim
                if (sentiment == "unknown")
                {
                    sentiment = sseText;
                }
            }
        }
        else
        {
            // event_id gelemediyse fallback olarak ilk JSON'u kaydediyoruz
            sentiment = postJson;
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
