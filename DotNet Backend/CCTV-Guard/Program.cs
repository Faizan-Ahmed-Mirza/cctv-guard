using System.Text;
using System.Threading.RateLimiting;
using CCTV_Guard.Data;
using CCTV_Guard.Filters;
using CCTV_Guard.Hubs;
using CCTV_Guard.Middleware;
using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<CameraService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddSingleton<HubNotificationService>();  // Singleton — used by background service
builder.Services.AddSingleton<CameraStreamService>();

// HTTP client for Python AI microservice
builder.Services.AddHttpClient("AiService", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000");
    // 30s timeout — face recognition on CPU can take 5-15s, YOLO adds another 1-3s
    // 10s was too short and triggered the circuit breaker constantly
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Separate HTTP client for face registration — needs longer timeout
// because Facenet512 model loads on first call (~92MB weights) and
// face detection + embedding extraction can take 20-40 seconds on CPU.
builder.Services.AddHttpClient("AiServiceFace", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000");
    client.Timeout = TimeSpan.FromSeconds(90);
});

// HTTP client for camera health checks (IP Webcam probe)
builder.Services.AddHttpClient("HealthCheck", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.Add("User-Agent", "CctvGuard-HealthCheck/1.0");
});

// Background service — auto-detects camera online/offline status
builder.Services.AddHostedService<CameraHealthCheckService>();

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(key),
        ValidateIssuer           = true,
        ValidIssuer              = builder.Configuration["Jwt:Issuer"],
        ValidateAudience         = true,
        ValidAudience            = builder.Configuration["Jwt:Audience"],
        ValidateLifetime         = true,
        ClockSkew                = TimeSpan.Zero
    };

    // Allow SignalR to receive JWT via query string (?access_token=...)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var accessToken = ctx.Request.Query["access_token"];
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                ctx.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("CctvGuardPolicy", policy =>
        policy.SetIsOriginAllowed(_ => true)  // allow any origin including file://
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ── Rate Limiting — max 10 login attempts per IP per minute ───────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("LoginPolicy", limiter =>
    {
        limiter.Window            = TimeSpan.FromMinutes(1);
        limiter.PermitLimit       = 10;
        limiter.QueueLimit        = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024 * 5; // 5 MB — enough for any JPEG frame
});

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "CCTV Guard API",
        Version     = "v1",
        Description = "AI-Powered CCTV Guard — Backend REST API"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter your JWT token. Example: eyJhbGci..."
    });

    // Only show lock icon on [Authorize] endpoints
    c.OperationFilter<AuthorizeCheckOperationFilter>();
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
// Seed only the minimum required system config (AiSettings row).
// All other data (users, cameras, incidents) comes from the real database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db);
}

// ── Pre-download FFmpeg binaries (one-time, ~70 MB, cached afterwards) ────────
var streamService = app.Services.GetRequiredService<CameraStreamService>();
await streamService.EnsureFfmpegAsync();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CCTV Guard API v1");
        c.RoutePrefix = string.Empty;
    });
}

// CORS must come before everything else so preflight OPTIONS requests
// are handled before any redirect or auth middleware touches them
app.UseCors("CctvGuardPolicy");

// Only redirect to HTTPS in production — in dev Angular calls HTTP directly
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ── SignalR Hub endpoints ─────────────────────────────────────────────────────
// UseWebSockets must be called before MapHub so the WebSocket upgrade works.
// Required when Angular uses skipNegotiation: true + WebSockets-only transport.
app.UseWebSockets();
app.MapHub<AlertsHub>("/hubs/alerts");
app.MapHub<CameraStreamHub>("/hubs/camera-stream");

app.Run();
