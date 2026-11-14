using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auth;
using ProductAuction.API.DTOs.Profile;
using ProductAuction.API.Models;
using ProductAuction.API.Services;
using ProductAuction.API.Services.Auth;
using System.Security.Claims;

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
        private readonly IJwtTokenService _jwtTokenService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            IConfiguration configuration,
            IImageUploadService imageUploadService,
            IJwtTokenService jwtTokenService,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
            _imageUploadService = imageUploadService;
            _jwtTokenService = jwtTokenService;
            _logger = logger;
        }


        // POST: api/auth/register
        // Creates an account, assigns a role, and saves the profile and wallet.
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);

            if (existingUser != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Email is already registered."
                });
            }

            if (dto.Role != "Buyer" && dto.Role != "Seller")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only Buyer and Seller roles are allowed."
                });
            }

            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email
            };

            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Registration failed.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            var roleResult = await _userManager.AddToRoleAsync(user, dto.Role);

            if (!roleResult.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Role assignment failed.",
                    errors = roleResult.Errors.Select(e => e.Description)
                });
            }

            var profile = new UserProfile
            {
                UserId = user.Id,
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            };

            var wallet = new Wallet
            {
                UserId = user.Id,
                Balance = 0,
                ReservedBalance = 0
            };

            _context.UserProfiles.Add(profile);
            _context.Wallets.Add(wallet);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Registration successful.",
                data = new
                {
                    email = user.Email,
                    role = dto.Role
                }
            });
        }


        // POST: api/auth/login
        // Validates email/password and returns a JWT token if successful.
        [EnableRateLimiting("login")]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
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

            var passwordIsValid = await _userManager.CheckPasswordAsync(user, dto.Password);
            if (!passwordIsValid)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid email or password."
                });
            }

            var tokenData = await _jwtTokenService.GenerateTokenAsync(user);
            return Ok(new
            {
                status = 200,
                message = "Login successful.",
                data = tokenData
            });
        }

        // POST: api/auth/google-login
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.IdToken))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "ID token is required."
                });
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                var clientId = (_configuration["Authentication:Google:ClientId"] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    _logger.LogError("Google login is not configured: client ID is missing.");
                    return StatusCode(StatusCodes.Status500InternalServerError, new
                    {
                        status = StatusCodes.Status500InternalServerError,
                        message = "Google login is not configured."
                    });
                }

                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken);
                if (!payload.AudienceAsList.Any(audience =>
                        string.Equals(audience, clientId, StringComparison.Ordinal)))
                {
                    throw new InvalidJwtException("Google ID token audience does not match the configured client ID.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google token validation failed.");
                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid Google token.",
                    detail = ex.Message
                });
            }

            var email = payload.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Google account email is missing."
                });
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = payload.EmailVerified
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Failed to create user from Google account.",
                        errors = createResult.Errors.Select(e => e.Description)
                    });
                }

                var roleResult = await _userManager.AddToRoleAsync(user, "Buyer");
                if (!roleResult.Succeeded)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Failed to assign default role.",
                        errors = roleResult.Errors.Select(e => e.Description)
                    });
                }

                _context.UserProfiles.Add(new UserProfile
                {
                    UserId = user.Id,
                    FullName = string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name,
                    CreatedAt = DateTime.UtcNow
                });
                _context.Wallets.Add(new Wallet { UserId = user.Id, Balance = 0, ReservedBalance = 0 });
                await _context.SaveChangesAsync();
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Account is locked."
                });
            }

            var tokenData = await _jwtTokenService.GenerateTokenAsync(user);
            return Ok(new
            {
                status = 200,
                message = "Google login successful.",
                data = tokenData
            });
        }

        // GET: api/auth/test-token
        // Requires a valid JWT token
        [Authorize]
        [HttpGet("test-token")]
        public IActionResult TestToken()
        {
            return Ok(new
            {
                status = 200,
                message = "JWT authentication successful.",
                data = new
                {
                    userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    email = User.FindFirst(ClaimTypes.Email)?.Value,
                    role = User.FindFirst(ClaimTypes.Role)?.Value
                }
            });
        }


        // GET: api/auth/me
        // Returns the current user's account and profile info. 
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found."
                });
            }

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User account was not found."
                });
            }

            var profile = await _context.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                status = 200,
                message = "Current user retrieved successfully.",

                data = new
                {
                    account = new
                    {
                        userId = user.Id,
                        email = user.Email,
                        emailConfirmed = user.EmailConfirmed,
                        phoneNumber = user.PhoneNumber,
                        roles
                    },

                    profile = new
                    {
                        fullName = profile?.FullName,
                        profilePhotoUrl = profile?.ProfilePhotoUrl,
                        address = profile?.Address,
                        city = profile?.City,
                        nationalIdNumber = profile?.NationalIdNumber,
                        nationalIdFrontImageUrl = profile?.NationalIdFrontImageUrl,
                        nationalIdBackImageUrl = profile?.NationalIdBackImageUrl,
                        verificationStatus = profile?.VerificationStatus,
                        rejectionReason = profile?.RejectionReason,
                        createdAt = profile?.CreatedAt,
                        updatedAt = profile?.UpdatedAt
                    }
                }
            });
        }


        // PUT: api/auth/me
        // Updates the editable profile fields
        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromForm] UpdateProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found."
                });
            }

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User profile not found."
                });
            }

            // nupdate the UserProfile fields
            profile.FullName = dto.FullName;

            if (dto.ProfilePhotoUrl != null)
            {
                profile.ProfilePhotoUrl = dto.ProfilePhotoUrl;
            }

            profile.Address = dto.Address;
            profile.City = dto.City;
            profile.NationalIdNumber = dto.NationalIdNumber;

            // upload new NID images
            if (dto.NationalIdFrontImage != null)
            {
                profile.NationalIdFrontImageUrl = await _imageUploadService.UploadImageAsync(dto.NationalIdFrontImage);
            }

            if (dto.NationalIdBackImage != null)
            {
                profile.NationalIdBackImageUrl = await _imageUploadService.UploadImageAsync(dto.NationalIdBackImage);
            }

            profile.UpdatedAt = DateTime.UtcNow;

            // update the phone number on the Identity
            if (!string.IsNullOrEmpty(dto.PhoneNumber))
            {
                var user = await _userManager.FindByIdAsync(userId);

                if (user != null)
                {
                    var phoneResult = await _userManager.SetPhoneNumberAsync(user, dto.PhoneNumber);

                    if (!phoneResult.Succeeded)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Failed to update phone number."
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found."
                });
            }

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User account not found."
                });
            }

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
