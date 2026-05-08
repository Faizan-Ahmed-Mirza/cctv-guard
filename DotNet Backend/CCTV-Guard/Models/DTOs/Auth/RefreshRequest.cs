using System.ComponentModel.DataAnnotations;

namespace CCTV_Guard.Models.DTOs.Auth;

public class RefreshRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}
