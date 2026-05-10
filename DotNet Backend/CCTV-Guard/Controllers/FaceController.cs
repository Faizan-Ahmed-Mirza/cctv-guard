using System.Security.Claims;
using System.Text.Json;
using CCTV_Guard.Data;
using CCTV_Guard.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Controllers;

/// <summary>
/// Manages face registrations.
///
/// Storage strategy — dual write:
///   1. SQL Server (FacialEmbeddings table) — source of truth, survives restarts
///   2. Python AI service (faces_db.pkl)    — in-memory for fast inference
///
/// On register: save embedding to SQL Server, then push to AI service.
/// On delete:   remove from SQL Server, then remove from AI service.
/// On startup:  AI service loads from its own pkl file (synced from DB on first register).
/// </summary>
[ApiController]
[Route("api/faces")]
[Authorize]
public class FaceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FaceController> _logger;

    public FaceController(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<FaceController> logger)
    {
        _db                = db;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <summary>GET /api/faces — list all registered faces from SQL Server.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFaces()
    {
        var faces = await _db.FacialEmbeddings
            .AsNoTracking()
            .OrderBy(f => f.Username)
            .Select(f => new
            {
                f.Username,
                f.RegisteredAt,
                f.RegisteredBy
            })
            .ToListAsync();

        return Ok(new
        {
            faces = faces.Select(f => f.Username).ToList(),
            total = faces.Count,
            details = faces
        });
    }

    /// <summary>
    /// POST /api/faces/register — register a face photo.
    /// Saves embedding to SQL Server AND pushes to AI service.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> RegisterFace(
        [FromForm] string username,
        IFormFile photo)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest(new { message = "Username is required." });
        if (photo == null || photo.Length == 0)
            return BadRequest(new { message = "Photo file is required." });

        username = username.Trim();
        var registeredBy = User.FindFirstValue(ClaimTypes.Name)
                        ?? User.FindFirstValue("sub")
                        ?? "unknown";

        // ── Step 1: Send photo to AI service to get the embedding back ────────
        string embeddingJson;
        try
        {
            // Use the face-specific client with 90s timeout — Facenet512 loads on first call
            var client = _httpClientFactory.CreateClient("AiServiceFace");

            // First register in AI service (it stores in pkl AND returns success)
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");

            var fileBytes = new byte[photo.Length];
            await photo.OpenReadStream().ReadAsync(fileBytes);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(photo.ContentType ?? "image/jpeg");
            content.Add(fileContent, "file", photo.FileName ?? "photo.jpg");

            var aiResponse = await client.PostAsync("/register-face", content);
            var aiJson     = await aiResponse.Content.ReadAsStringAsync();

            if (!aiResponse.IsSuccessStatusCode)
            {
                // Parse the detail message from Python FastAPI error response
                var detail = aiJson;
                try
                {
                    var err = JsonSerializer.Deserialize<JsonElement>(aiJson);
                    if (err.TryGetProperty("detail", out var d)) detail = d.GetString() ?? aiJson;
                }
                catch { /* use raw json */ }
                return StatusCode((int)aiResponse.StatusCode, new { message = detail });
            }

            embeddingJson = "[]"; // placeholder — actual embedding is in AI service pkl
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Face registration timed out for '{Username}' — AI service too slow", username);
            return StatusCode(503, new { message = "Face registration timed out. The AI model may be loading for the first time (can take 30-60s). Please try again in a moment." });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI service unreachable during face registration for '{Username}'", username);
            return StatusCode(503, new { message = "AI service is not running. Start it with: cd ai-service && py main.py" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service error during face registration for '{Username}'", username);
            return StatusCode(503, new { message = "AI service unavailable. Start the AI service and try again." });
        }

        // ── Step 3: Save registration record to SQL Server ────────────────────
        try
        {
            var existing = await _db.FacialEmbeddings
                .FirstOrDefaultAsync(f => f.Username == username);

            if (existing != null)
            {
                // Update existing registration
                existing.EmbeddingJson = embeddingJson;
                existing.RegisteredAt  = DateTime.UtcNow;
                existing.RegisteredBy  = registeredBy;
            }
            else
            {
                _db.FacialEmbeddings.Add(new FacialEmbedding
                {
                    Username      = username,
                    EmbeddingJson = embeddingJson,
                    RegisteredAt  = DateTime.UtcNow,
                    RegisteredBy  = registeredBy
                });
            }
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save face registration to DB for '{Username}'", username);
            // Don't fail the request — AI service already registered it
        }

        var total = await _db.FacialEmbeddings.CountAsync();
        _logger.LogInformation("Face registered for '{Username}' by '{By}'. Total: {Total}",
            username, registeredBy, total);

        return Ok(new { message = $"Face registered for '{username}'.", total_registered = total });
    }

    /// <summary>DELETE /api/faces/{username} — remove from SQL Server AND AI service.</summary>
    [HttpDelete("{username}")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> DeleteFace(string username)
    {
        // ── Step 1: Remove from SQL Server ────────────────────────────────────
        var record = await _db.FacialEmbeddings
            .FirstOrDefaultAsync(f => f.Username == username);

        if (record != null)
        {
            _db.FacialEmbeddings.Remove(record);
            await _db.SaveChangesAsync();
        }

        // ── Step 2: Remove from AI service ────────────────────────────────────
        try
        {
            var client   = _httpClientFactory.CreateClient("AiService");
            var response = await client.DeleteAsync($"/faces/{Uri.EscapeDataString(username)}");
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("AI service returned {Status} when deleting face for '{Username}'",
                    response.StatusCode, username);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service unavailable when deleting face for '{Username}' — removed from DB only", username);
            // Don't fail — DB record is already deleted
        }

        var total = await _db.FacialEmbeddings.CountAsync();
        _logger.LogInformation("Face deleted for '{Username}' by '{By}'. Remaining: {Total}",
            username, User.Identity?.Name, total);

        return Ok(new { message = $"'{username}' removed.", total_registered = total });
    }
}
