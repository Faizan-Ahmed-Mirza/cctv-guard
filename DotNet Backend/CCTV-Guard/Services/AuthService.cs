using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Auth;
using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthService(AppDbContext db, TokenService tokenService,
        IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _tokenService = tokenService;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.Status == "active");

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        // Update last login
        user.LastLogin = DateTime.UtcNow;

        // Create session record
        var ctx = _httpContextAccessor.HttpContext;
        var session = new UserSession
        {
            UserId = user.Id,
            LoginAt = DateTime.UtcNow,
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = ctx?.Request.Headers.UserAgent.ToString()
        };
        _db.UserSessions.Add(session);

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();
        var refreshExpiryDays = int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "7");

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays)
        };
        _db.RefreshTokens.Add(refreshToken);

        await _db.SaveChangesAsync();

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresIn = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "60") * 60,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                Status = user.Status,
                CreatedAt = user.CreatedAt,
                LastLogin = user.LastLogin
            }
        };
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshTokenValue)
    {
        var token = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshTokenValue
                && !r.IsRevoked
                && r.ExpiresAt > DateTime.UtcNow);

        if (token == null || token.User.Status == "suspended")
            return null;

        // Revoke old token
        token.IsRevoked = true;

        // Issue new tokens
        var accessToken = _tokenService.GenerateAccessToken(token.User);
        var newRefreshValue = _tokenService.GenerateRefreshToken();
        var refreshExpiryDays = int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "7");

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = token.UserId,
            Token = newRefreshValue,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays)
        });

        await _db.SaveChangesAsync();

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshValue,
            ExpiresIn = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "60") * 60,
            User = new UserDto
            {
                Id = token.User.Id,
                Username = token.User.Username,
                Email = token.User.Email,
                Role = token.User.Role,
                Status = token.User.Status,
                CreatedAt = token.User.CreatedAt,
                LastLogin = token.User.LastLogin
            }
        };
    }

    public async Task LogoutAsync(Guid userId, string refreshTokenValue)
    {
        // Revoke refresh token
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshTokenValue && r.UserId == userId);
        if (token != null)
            token.IsRevoked = true;

        // Close open session
        var session = await _db.UserSessions
            .Where(s => s.UserId == userId && s.LogoutAt == null)
            .OrderByDescending(s => s.LoginAt)
            .FirstOrDefaultAsync();

        if (session != null)
        {
            session.LogoutAt = DateTime.UtcNow;
            session.DurationMin = (int)(session.LogoutAt.Value - session.LoginAt).TotalMinutes;
        }

        await _db.SaveChangesAsync();
    }
}
