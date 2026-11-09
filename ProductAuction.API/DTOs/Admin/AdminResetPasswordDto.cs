using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Admin;

public class AdminResetPasswordDto
{
    [Required, MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}
