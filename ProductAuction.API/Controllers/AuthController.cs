using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auth;
using ProductAuction.API.DTOs.Profile;
using ProductAuction.API.Models;
using ProductAuction.API.Services;
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
        private readonly IImageUploadService _imageUploadService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            IConfiguration configuration,
            IImageUploadService imageUploadService,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
            _imageUploadService = imageUploadService;
            _logger = logger;
        }


        // POST: api/auth/register
        // Creates an account, assigns a role, and saves the profile and wallet.
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            // _logger.LogInformation("Register called for {Email} (role={Role}).", dto.Email, dto.Role);

            // check if this email is already taken
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);

            if (existingUser != null)
            {
                // _logger.LogWarning("Register: email {Email} is already registered.", dto.Email);

                return BadRequest(new
                {
                    status = 400,
                    message = "Email is already registered."
                });
            }

            // public registration only allows Buyer or Seller
            if (dto.Role != "Buyer" && dto.Role != "Seller")
            {
                // _logger.LogWarning("Register: invalid role {Role} requested for {Email}.", dto.Role, dto.Email);

                return BadRequest(new
                {
                    status = 400,
                    message = "Only Buyer and Seller roles are allowed."
                });
            }

            // create the Identity user object
            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email
            };

            // create the account (Identity hashes the password for us)
            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
            {
                // _logger.LogWarning("Register: Identity create failed for {Email}: {Errors}",
                //     dto.Email, string.Join("; ", result.Errors.Select(e => e.Description)));

                return BadRequest(new
                {
                    status = 400,
                    message = "Registration failed.",
                    errors = result.Errors
                });
            }

            // assign the selected role
            var roleResult = await _userManager.AddToRoleAsync(user, dto.Role);

            if (!roleResult.Succeeded)
            {
                // _logger.LogWarning("Register: role assignment '{Role}' failed for {Email}: {Errors}",
                //     dto.Role, dto.Email, string.Join("; ", roleResult.Errors.Select(e => e.Description)));

                return BadRequest(new
                {
                    status = 400,
                    message = "Role assignment failed.",
                    errors = roleResult.Errors
                });
            }

            // create the user's profile
            var profile = new UserProfile
            {
                UserId = user.Id,
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            };

            // and an empty wallet
            var wallet = new Wallet
            {
                UserId = user.Id,
                Balance = 0,
                ReservedBalance = 0
            };

            _context.UserProfiles.Add(profile);
            _context.Wallets.Add(wallet);

            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(await _context.UserProfiles.AnyAsync(p => p.UserId == user.Id));
            // System.Diagnostics.Debug.Assert(await _context.Wallets.AnyAsync(w => w.UserId == user.Id));
            // System.Diagnostics.Debug.Assert(await _userManager.IsInRoleAsync(user, dto.Role));

            // _logger.LogInformation("Register: account {Email} created with role {Role}.", dto.Email, dto.Role);

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
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            // _logger.LogInformation("Login attempt for {Email}.", dto.Email);

            // find the account by email
            var user = await _userManager.FindByEmailAsync(dto.Email);

            // don't reveal whether the email or password was wrong
            if (user == null)
            {
                // _logger.LogWarning("Login failed: no account for {Email}.", dto.Email);

                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid email or password."
                });
            }

            // locked accounts can't log in even with the right password
            if (await _userManager.IsLockedOutAsync(user))
            {
                // _logger.LogWarning("Login blocked: account {Email} is locked out.", dto.Email);

                return Unauthorized(new
                {
                    status = 401,
                    message = "Account is locked."
                });
            }

            // check the submitted password
            var passwordIsValid = await _userManager.CheckPasswordAsync(user, dto.Password);

            if (!passwordIsValid)
            {
                // _logger.LogWarning("Login failed: invalid password for {Email}.", dto.Email);

                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid email or password."
                });
            }

            // get the roles assigned to this user
            var roles = await _userManager.GetRolesAsync(user);

            // put the user's id, email, and roles into the token
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // signing key comes from config
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            // pick the signing algorithm
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // token lasts 2 hours
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials);

            // turn it into a string for the client
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            // System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(tokenString));
            // System.Diagnostics.Debug.Assert(claims.Any(c => c.Type == ClaimTypes.NameIdentifier && c.Value == user.Id));

            // _logger.LogInformation("Login successful for {Email} (roles: {Roles}).", dto.Email, string.Join(",", roles));

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
        // Requires a valid JWT token — just to confirm auth is working.
        [Authorize]
        [HttpGet("test-token")]
        public IActionResult TestToken()
        {
            // _logger.LogInformation("TestToken called by user {UserId}.", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

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
        // Returns the current user's account and profile info.
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            // grab the user id from the validated JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                // _logger.LogWarning("Me: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("Me called for user {UserId}.", userId);

            // find the Identity account
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                // _logger.LogWarning("Me: account {UserId} not found.", userId);

                return Unauthorized(new
                {
                    status = 401,
                    message = "User account was not found."
                });
            }

            // find the profile linked to this account
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            // get all roles for this account
            var roles = await _userManager.GetRolesAsync(user);

            // System.Diagnostics.Debug.Assert(roles.Any());

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
        // Updates the editable profile fields (and optionally uploads NID images).
        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromForm] UpdateProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                // _logger.LogWarning("UpdateMe: user identity missing from token.");

                return Unauthorized();
            }

            // _logger.LogInformation("UpdateMe called for user {UserId}.", userId);

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                // _logger.LogWarning("UpdateMe: profile for user {UserId} not found.", userId);

                return NotFound(new
                {
                    message = "User profile not found."
                });
            }

            // 1. update the UserProfile fields
            profile.FullName = dto.FullName;
            profile.ProfilePhotoUrl = dto.ProfilePhotoUrl;
            profile.Address = dto.Address;
            profile.City = dto.City;
            profile.NationalIdNumber = dto.NationalIdNumber;

            // upload new NID images if any were sent
            if (dto.NationalIdFrontImage != null)
            {
                profile.NationalIdFrontImageUrl = await _imageUploadService.UploadImageAsync(dto.NationalIdFrontImage);

                // _logger.LogDebug("UpdateMe: uploaded new NID front image for user {UserId}.", userId);
            }

            if (dto.NationalIdBackImage != null)
            {
                profile.NationalIdBackImageUrl = await _imageUploadService.UploadImageAsync(dto.NationalIdBackImage);

                // _logger.LogDebug("UpdateMe: uploaded new NID back image for user {UserId}.", userId);
            }

            profile.UpdatedAt = DateTime.UtcNow;

            // 2. update the phone number on the Identity side
            if (!string.IsNullOrEmpty(dto.PhoneNumber))
            {
                var user = await _userManager.FindByIdAsync(userId);

                if (user != null)
                {
                    var phoneResult = await _userManager.SetPhoneNumberAsync(user, dto.PhoneNumber);

                    if (!phoneResult.Succeeded)
                    {
                        // _logger.LogWarning("UpdateMe: failed to update phone number for user {UserId}: {Errors}",
                        //     userId, string.Join("; ", phoneResult.Errors.Select(e => e.Description)));

                        return BadRequest(new
                        {
                            message = "Failed to update phone number."
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();

            // System.Diagnostics.Debug.Assert(profile.UpdatedAt != null);
            // System.Diagnostics.Debug.Assert(profile.FullName == dto.FullName);

            // _logger.LogInformation("UpdateMe: profile for user {UserId} updated successfully.", userId);

            return Ok(new
            {
                status = 200,
                message = "Profile updated successfully."
            });
        }


        // POST: api/auth/change-password
        // Changes the password of the logged-in user.
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            // grab the user id from the validated JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                // _logger.LogWarning("ChangePassword: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("ChangePassword called for user {UserId}.", userId);

            // find the current Identity account
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                // _logger.LogWarning("ChangePassword: account {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "User account not found."
                });
            }

            // Identity validates the current password and applies the password rules
            var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

            if (!result.Succeeded)
            {
                // _logger.LogWarning("ChangePassword: failed for user {UserId}: {Errors}",
                //     userId, string.Join("; ", result.Errors.Select(e => e.Description)));

                return BadRequest(new
                {
                    status = 400,
                    message = "Password change failed.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            // _logger.LogInformation("ChangePassword: password updated for user {UserId}.", userId);

            return Ok(new
            {
                status = 200,
                message = "Password changed successfully."
            });
        }
    }
}