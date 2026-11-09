using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Role;

public class AssignRoleDto
{
    [Required]
    public string RoleName { get; set; } = string.Empty;
}
