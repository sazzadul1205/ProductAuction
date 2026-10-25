
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auth;
using ProductAuction.API.DTOs.Profile;
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
        // Creates an account, assigns a role, and saves the profile and wallet.
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            // Check whether the email is already registered.
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);

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

            // Create the Identity user object.
            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email
            };

            // Start a transaction so all database changes succeed or roll back together.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Create the user's Identity account.
                // Identity securely hashes the password.
                var result = await _userManager.CreateAsync(user, dto.Password);

                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        status = 400,
                        message = "Registration failed.",
                        errors = result.Errors
                    });
                }

                // Assign the selected role to the new account.
                var roleResult = await _userManager.AddToRoleAsync(user, dto.Role);

                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync();

                    return BadRequest(new
                    {
                        status = 400,
                        message = "Role assignment failed.",
                        errors = roleResult.Errors
                    });
                }

                // Create the user's profile.
                var profile = new UserProfile
                {
                    UserId = user.Id,
                    FullName = dto.FullName,
                    CreatedAt = DateTime.UtcNow
                };

                // Create an empty wallet for the new user.
                var wallet = new Wallet
                {
                    UserId = user.Id,
                    Balance = 0,
                    ReservedBalance = 0
                };

                _context.UserProfiles.Add(profile);
                _context.Wallets.Add(wallet);

                // Save the profile and wallet.
                await _context.SaveChangesAsync();

                // Commit only after every required operation succeeds.
                await transaction.CommitAsync();

                return Ok(new
                {
                    status = 200,
                    message = "Registration successful.",
                    email = user.Email,
                    role = dto.Role
                });
            }
            catch
            {
                // Undo the database changes if an unexpected error occurs.
                await transaction.RollbackAsync();

                throw;
            }
        }


        // POST: api/auth/login
        // Checks the credentials and returns a JWT token.
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            // Find the account by email.
            var user = await _userManager.FindByEmailAsync(dto.Email);

            // Do not reveal whether the email or password was incorrect.
            if (user == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid email or password."
                });
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Account is locked."
                });
            }
            // Check the submitted password.
            var passwordIsValid = await _userManager.CheckPasswordAsync(user, dto.Password);

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


        // GET: api/auth/me
        // Returns the current user's account and profile information.
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            // Get the user ID from the validated JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the Identity account.
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User account was not found."
                });
            }

            // Find the profile linked to this account.
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            // Get all roles assigned to this account.
            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                status = 200,
                message = "Current user retrieved successfully.",

                account = new
                {
                    userId = user.Id,
                    email = user.Email,
                    emailConfirmed = user.EmailConfirmed,
                    phoneNumber = user.PhoneNumber,
                    roles
                },

                profile = profile == null ? null : new
                {
                    fullName = profile.FullName,
                    profilePhotoUrl = profile.ProfilePhotoUrl,
                    address = profile.Address,
                    city = profile.City,
                    nationalIdNumber = profile.NationalIdNumber,
                    nationalIdFrontImageUrl = profile.NationalIdFrontImageUrl,
                    nationalIdBackImageUrl = profile.NationalIdBackImageUrl,
                    verificationStatus = profile.VerificationStatus,
                    rejectionReason = profile.RejectionReason,
                    createdAt = profile.CreatedAt,
                    updatedAt = profile.UpdatedAt
                }
            });
        }


        // PUT: api/auth/me
        // Updates the editable profile of the currently logged-in user.
        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe(UpdateProfileDto dto)
        {
            // Get the current user's ID from the validated JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the current user's profile.
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User profile not found."
                });
            }

            // Update the fields allowed by UpdateProfileDto.
            profile.FullName = dto.FullName;
            profile.ProfilePhotoUrl = dto.ProfilePhotoUrl;
            profile.Address = dto.Address;
            profile.City = dto.City;
            profile.NationalIdNumber = dto.NationalIdNumber;
            profile.NationalIdFrontImageUrl = dto.NationalIdFrontImageUrl;
            profile.NationalIdBackImageUrl = dto.NationalIdBackImageUrl;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Profile updated successfully."
            });
        }


        // POST: api/auth/change-password
        // Changes the password of the currently logged-in user.
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            // Get the current user's ID from the validated JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the current Identity account.
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User account not found."
                });
            }

            // and apply its configured password rules.
            var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Password change failed.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Password changed successfully."
            });
        }
    }
}
