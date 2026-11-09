using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Role;

public class CreateRoleDto
{
    [Required, MinLength(2), MaxLength(50)]
    public string Name { get; set; } = string.Empty;
}
