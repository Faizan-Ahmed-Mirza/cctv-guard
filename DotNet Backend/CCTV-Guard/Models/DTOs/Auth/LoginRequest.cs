using System.ComponentModel.DataAnnotations;

namespace CCTV_Guard.Models.DTOs.Auth;

public class LoginRequest
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}
