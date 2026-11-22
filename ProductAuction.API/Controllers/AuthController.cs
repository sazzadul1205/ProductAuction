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
        private readonly ILogger<AuthController> _logger;
        private readonly IJwtTokenService _jwtTokenService;

        public AuthController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            IConfiguration configuration,
            IImageUploadService imageUploadService,
            ILogger<AuthController> logger,
            IJwtTokenService jwtTokenService)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
            _imageUploadService = imageUploadService;
            _logger = logger;
            _jwtTokenService = jwtTokenService;
        }


        // POST: api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Invalid registration data.",
                    errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                });
            }

            var existingUser = await _userManager.FindByEmailAsync(dto.Email);

            if (existingUser != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Email is already registered."
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

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };

            var tokenString = _jwtTokenService.GenerateAccessToken(claims);

            var refreshToken = _jwtTokenService.GenerateRefreshToken();
            var rt = new RefreshToken
            {
                Token = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            _context.RefreshTokens.Add(rt);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Registration successful. Please complete your profile setup.",
                data = new
                {
                    email = user.Email,
                    token = tokenString,
                    refreshToken,
                    expiresInMinutes = 120,
                    profileSetupRequired = true
                }
            });
        }


        // POST: api/auth/login
        // Checks the credentials and returns a JWT token.
        [HttpPost("login")]
        [EnableRateLimiting("login")]
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

            var roles = await _userManager.GetRolesAsync(user);

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            
            var isProfileCompleted = profile != null && profile.IsProfileSetupCompleted;

            var isStaff = roles.Contains("Admin") || roles.Contains("Super Admin");

            // Staff accounts are pre-provisioned with a role and do not require buyer/seller onboarding.
            var profileSetupRequired = !isStaff && !isProfileCompleted;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // Generate access token via service
            var tokenString = _jwtTokenService.GenerateAccessToken(claims);

            // Generate refresh token if RememberMe is true
            string? refreshToken = null;
            if (dto.RememberMe)
            {
                refreshToken = _jwtTokenService.GenerateRefreshToken();
                var rt = new RefreshToken
                {
                    Token = refreshToken,
                    UserId = user.Id,
                    ExpiresAt = DateTime.UtcNow.AddDays(30)
                };
                _context.RefreshTokens.Add(rt);
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                status = 200,
                message = "Login successful.",
                data = new
                {
                    email = user.Email,
                    roles,
                    token = tokenString,
                    refreshToken,
                    expiresInMinutes = 120,
                    profileSetupRequired
                }
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

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
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
                        isProfileSetupCompleted = profile?.IsProfileSetupCompleted ?? false,
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

        // POST: api/auth/refresh
        // Refreshes the access token using a refresh token
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
        {
            var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

            if (refreshToken == null || !refreshToken.IsActive)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Invalid or expired refresh token."
                });
            }

            var user = await _userManager.FindByIdAsync(refreshToken.UserId);
            if (user == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User not found."
                });
            }

            // Revoke the old refresh token
            refreshToken.IsRevoked = true;

            // Generate new tokens
            var roles = await _userManager.GetRolesAsync(user);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var tokenString = _jwtTokenService.GenerateAccessToken(claims);

            var newRefreshToken = _jwtTokenService.GenerateRefreshToken();
            var rt = new RefreshToken
            {
                Token = newRefreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            _context.RefreshTokens.Add(rt);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Token refreshed successfully.",
                data = new
                {
                    token = tokenString,
                    refreshToken = newRefreshToken,
                    expiresInMinutes = 120
                }
            });
        }

        // POST: api/auth/logout
        // Revokes the provided refresh token
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutDto dto)
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

            if (dto.RefreshToken == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Refresh token is required."
                });
            }

            var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken && rt.UserId == userId);

            if (refreshToken != null && refreshToken.IsActive)
            {
                refreshToken.IsRevoked = true;
                refreshToken.IsActive = false;
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                status = 200,
                message = "Logged out successfully."
            });
        }
    }
}