
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auth;
using ProductAuction.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _context;

        // Reads JWT settings from appsettings.json.
        private readonly IConfiguration _configuration;

        public AuthController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
        }

        // POST: api/auth/register
        // Creates an account, assigns a role, and saves the profile.
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            // Check whether the email is already registered.
            var existingUser =
                await _userManager.FindByEmailAsync(dto.Email);

            if (existingUser != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Email is already registered."
                });
            }

            // Public registration only allows Buyer or Seller.
            if (dto.Role != "Buyer" && dto.Role != "Seller")
                {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only Buyer and Seller roles are allowed."
                });
            }

            //if (dto.Role != "Buyer" && dto.Role != "Seller" && dto.Role != "Admin")
            //{
            //    return BadRequest(new
            //    {
            //        status = 400,
            //        message = "Only Buyer and Seller roles are allowed."
            //    });
            //}

            // Create the user's Identity account.
            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email
            };

            // Identity securely hashes the password before storing it.
            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Registration failed.",
                    errors = result.Errors
                });
            }

            // Assign the selected role to the new account.
            var roleResult = await _userManager.AddToRoleAsync(user, dto.Role);

            // Remove the account if role assignment fails.
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);

                return BadRequest(new
                {
                    status = 400,
                    message = "Role assignment failed.",
                    errors = roleResult.Errors
                });
            }

            // Save additional user information in UserProfiles.
            var profile = new UserProfile
            {
                UserId = user.Id,
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync();

            // Return a successful registration response.
            return Ok(new
            {
                status = 200,
                message = "Registration successful.",
                email = user.Email,
                role = dto.Role
            });
        }

        // POST: api/auth/login
        // Checks the credentials and returns a JWT token.
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            // Find the account by email.
            var user =await _userManager.FindByEmailAsync(dto.Email);

            // Do not reveal whether the email or password was incorrect.
            if (user == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid email or password."
                });
            }

            // Check the submitted password.
            var passwordIsValid =await _userManager.CheckPasswordAsync(user, dto.Password);

            if (!passwordIsValid)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid email or password."
                });
            }

            // Get the roles assigned to this user.
            var roles = await _userManager.GetRolesAsync(user);

            // Add the user's ID, email, and roles to the token.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // Read the signing key from configuration.
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            // Choose the algorithm used to sign the token.
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Create a token that expires in two hours.
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials);

            // Convert the token into a string for the client.
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            // Return the token and account information.
            return Ok(new
            {
                status = 200,
                message = "Login successful.",
                email = user.Email,
                roles,
                token = tokenString
            });
        }

        // GET: api/auth/test-token
        // Requires a valid JWT token.
        [Authorize]
        [HttpGet("test-token")]
        public IActionResult TestToken()
        {
            return Ok(new
            {
                status = 200,
                message = "JWT authentication successful.",
                userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                email = User.FindFirst(ClaimTypes.Email)?.Value,
                role = User.FindFirst(ClaimTypes.Role)?.Value
            });
        }
    }
}
