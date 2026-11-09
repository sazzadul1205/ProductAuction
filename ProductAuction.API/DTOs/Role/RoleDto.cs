namespace ProductAuction.API.DTOs.Role;

public class RoleDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public int UserCount { get; set; }
}
