using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.DTOs.Role;

namespace ProductAuction.API.Controllers;

[Route("api/admin/roles")]
[ApiController]
[Authorize(Roles = "Admin")]
public class RoleController : ControllerBase
{
    // Roles that cannot be renamed or deleted.
    private static readonly HashSet<string> ProtectedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin", "Buyer", "Seller"
    };

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<IdentityUser> _userManager;

    public RoleController(RoleManager<IdentityRole> roleManager, UserManager<IdentityUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    private static bool IsProtected(string? name)
        => name != null && ProtectedRoles.Contains(name);


    // GET: api/admin/roles
    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync();

        var result = new List<RoleDto>(roles.Count);
        foreach (var r in roles)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(r.Name!);
            result.Add(new RoleDto
            {
                Id = r.Id,
                Name = r.Name ?? "",
                IsSystem = IsProtected(r.Name),
                UserCount = usersInRole.Count
            });
        }

        return Ok(new
        {
            status = 200,
            message = "Roles retrieved successfully.",
            data = result
        });
    }


    // GET: api/admin/roles/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRole(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null)
        {
            return NotFound(new { status = 404, message = "Role not found." });
        }

        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);

        return Ok(new
        {
            status = 200,
            message = "Role retrieved successfully.",
            data = new RoleDto
            {
                Id = role.Id,
                Name = role.Name ?? "",
                IsSystem = IsProtected(role.Name),
                UserCount = usersInRole.Count
            }
        });
    }


    // POST: api/admin/roles
    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto dto)
    {
        var name = dto.Name.Trim();

        if (await _roleManager.RoleExistsAsync(name))
        {
            return Conflict(new { status = 409, message = "Role already exists." });
        }

        var res = await _roleManager.CreateAsync(new IdentityRole(name));
        if (!res.Succeeded)
        {
            return BadRequest(new
            {
                status = 400,
                message = "Failed to create role.",
                errors = res.Errors.Select(e => e.Description)
            });
        }

        var role = await _roleManager.FindByNameAsync(name);

        return StatusCode(201, new
        {
            status = 201,
            message = "Role created successfully.",
            data = new RoleDto
            {
                Id = role!.Id,
                Name = role.Name ?? name,
                IsSystem = IsProtected(name),
                UserCount = 0
            }
        });
    }


    // PUT: api/admin/roles/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleDto dto)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null)
        {
            return NotFound(new { status = 404, message = "Role not found." });
        }

        if (IsProtected(role.Name))
        {
            return BadRequest(new
            {
                status = 400,
                message = "Admin, Buyer, and Seller roles cannot be renamed."
            });
        }

        var name = dto.Name.Trim();
        var existing = await _roleManager.FindByNameAsync(name);
        if (existing != null && existing.Id != id)
        {
            return Conflict(new { status = 409, message = "Role name already in use." });
        }

        role.Name = name;
        role.NormalizedName = _roleManager.NormalizeKey(name);

        var res = await _roleManager.UpdateAsync(role);
        if (!res.Succeeded)
        {
            return BadRequest(new
            {
                status = 400,
                message = "Failed to update role.",
                errors = res.Errors.Select(e => e.Description)
            });
        }

        return Ok(new { status = 200, message = "Role updated successfully." });
    }


    // DELETE: api/admin/roles/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRole(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null)
        {
            return NotFound(new { status = 404, message = "Role not found." });
        }

        if (IsProtected(role.Name))
        {
            return BadRequest(new
            {
                status = 400,
                message = "Admin, Buyer, and Seller roles cannot be deleted."
            });
        }

        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Count > 0)
        {
            return BadRequest(new
            {
                status = 400,
                message = "Cannot delete a role that is assigned to users."
            });
        }

        var res = await _roleManager.DeleteAsync(role);
        if (!res.Succeeded)
        {
            return BadRequest(new
            {
                status = 400,
                message = "Failed to delete role.",
                errors = res.Errors.Select(e => e.Description)
            });
        }

        return Ok(new { status = 200, message = "Role deleted successfully." });
    }
}