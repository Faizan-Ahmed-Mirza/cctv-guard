using System.ComponentModel.DataAnnotations;

namespace CCTV_Guard.Models.DTOs.User;

public class CreateUserRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Viewer";

    public string Status { get; set; } = "active";
}

public class UpdateUserRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? Password { get; set; }

    [Required]
    public string Role { get; set; } = "Viewer";

    public string Status { get; set; } = "active";
}

public class PatchStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}

public class PatchRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
