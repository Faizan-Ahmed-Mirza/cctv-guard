using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Auth;
using CCTV_Guard.Models.DTOs.User;
using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db) => _db = db;

    public async Task<List<UserDto>> GetAllAsync(string? role, string? status, string? search)
    {
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Username.Contains(search) || u.Email.Contains(search));

        return await query.OrderBy(u => u.Username).Select(u => ToDto(u)).ToListAsync();
    }

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        var u = await _db.Users.FindAsync(id);
        return u == null ? null : ToDto(u);
    }

    public async Task<(UserDto? user, string? error)> CreateAsync(CreateUserRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return (null, "Username already exists.");
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return (null, "Email already exists.");

        var user = new User
        {
            Username = req.Username,
            Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role,
            Status = req.Status
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return (ToDto(user), null);
    }

    public async Task<(UserDto? user, string? error)> UpdateAsync(Guid id, UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (null, "User not found.");

        if (await _db.Users.AnyAsync(u => u.Username == req.Username && u.Id != id))
            return (null, "Username already exists.");
        if (await _db.Users.AnyAsync(u => u.Email == req.Email && u.Id != id))
            return (null, "Email already exists.");

        user.Username = req.Username;
        user.Email = req.Email;
        user.Role = req.Role;
        user.Status = req.Status;
        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        await _db.SaveChangesAsync();
        return (ToDto(user), null);
    }

    public async Task<(bool ok, string? error)> PatchStatusAsync(Guid id, string status)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (false, "User not found.");

        if (status == "suspended" && user.Role == "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin" && u.Status == "active");
            if (adminCount <= 1) return (false, "Cannot suspend the last active Admin.");
        }

        user.Status = status;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> PatchRoleAsync(Guid id, string role)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (false, "User not found.");

        if (user.Role == "Admin" && role != "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin");
            if (adminCount <= 1) return (false, "Cannot demote the last Admin.");
        }

        user.Role = role;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (false, "User not found.");

        if (user.Role == "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin");
            if (adminCount <= 1) return (false, "Cannot delete the last Admin.");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private static UserDto ToDto(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Email = u.Email,
        Role = u.Role,
        Status = u.Status,
        CreatedAt = u.CreatedAt,
        LastLogin = u.LastLogin
    };
}
