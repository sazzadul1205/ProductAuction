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
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _context;
        private readonly IImageUploadService _imageUploadService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IWebHostEnvironment _env;

        public AuthController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            IConfiguration configuration,
            IImageUploadService imageUploadService,
            IEmailService emailService,
            ILogger<AuthController> logger,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
            _imageUploadService = imageUploadService;
            _emailService = emailService;
            _logger = logger;
            _env = env;
        }


        // POST: api/auth/register
        // Creates an account, assigns a role, saves profile/wallet, and sends confirmation email.
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
                Email = dto.Email,
                EmailConfirmed = false
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
                await _userManager.DeleteAsync(user);
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

            try
            {
                await SendConfirmationEmailAsync(user, dto.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email to {Email}", dto.Email);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    status = StatusCodes.Status503ServiceUnavailable,
                    message = "Registration completed, but the confirmation email could not be sent. Please try resending.",
                    data = new
                    {
                        email = user.Email,
                        role = dto.Role,
                        requiresEmailConfirmation = true,
                        emailSent = false
                    }
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Registration successful. Please check your email to confirm your account before signing in.",
                data = new
                {
                    email = user.Email,
                    role = dto.Role,
                    requiresEmailConfirmation = true,
                    emailSent = true
                }
            });
        }


        // POST: api/auth/login
        // Checks credentials and returns a JWT token only for verified accounts.
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

            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Email address is not confirmed. Please verify your email before signing in.",
                    data = new
                    {
                        requiresEmailConfirmation = true,
                        email = user.Email
                    }
                });
            }

            var roles = await _userManager.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
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

            return Ok(new
            {
                status = 200,
                message = "Login successful.",
                data = new
                {
                    email = user.Email,
                    roles,
                    token = tokenString,
                    expiresInMinutes = 120
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

        // POST: api/auth/forgot-password
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return Ok(new
                {
                    status = 200,
                    message = "If the email is registered, a password reset link has been sent."
                });
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebUtility.UrlEncode(token);
            var encodedEmail = WebUtility.UrlEncode(dto.Email);
            var frontendBase = ResolveFrontendBaseUrl();
            var resetLink = $"{frontendBase}/reset-password?email={encodedEmail}&token={encodedToken}";

            var body = EmailTemplates.BuildPasswordResetEmail(resetLink);

            try
            {
                await _emailService.SendEmailAsync(dto.Email, "Reset your password", body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", dto.Email);
                if (_env.IsDevelopment())
                {
                    _logger.LogWarning("DEV password reset link for {Email}: {Link}", dto.Email, resetLink);
                    return Ok(new
                    {
                        status = 200,
                        message = "Password reset link generated (email delivery failed in development). Use the provided link.",
                        data = new { resetLink }
                    });
                }
                return StatusCode(500, new
                {
                    status = 500,
                    message = "Failed to send password reset email. Check SMTP configuration."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "If the email is registered, a password reset link has been sent."
            });
        }

        // POST: api/auth/reset-password
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Invalid reset request."
                });
            }

            var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Password reset failed.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Password reset successful."
            });
        }

        // GET: api/auth/confirm-email
        // Verifies the email confirmation token (link target).
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] ConfirmEmailDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { status = 400, message = "Invalid confirmation request." });
            }

            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return BadRequest(new { status = 400, message = "Invalid confirmation request." });
            }

            if (user.EmailConfirmed)
            {
                return Ok(new { status = 200, message = "Email is already confirmed. You can sign in." });
            }

            var result = await _userManager.ConfirmEmailAsync(user, dto.Token);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Email confirmation failed. The link may be invalid or expired.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            return Ok(new { status = 200, message = "Email confirmed successfully. You can now sign in." });
        }

        // POST: api/auth/confirm-email
        [HttpPost("confirm-email")]
        public async Task<IActionResult> ConfirmEmailPost(ConfirmEmailDto dto)
        {
            return await ConfirmEmail(dto);
        }

        // POST: api/auth/resend-confirmation
        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return Ok(new { status = 200, message = "If the email is registered and unconfirmed, a confirmation link has been sent." });
            }

            if (user.EmailConfirmed)
            {
                return Ok(new { status = 200, message = "Email is already confirmed. You can sign in." });
            }

            try
            {
                await SendConfirmationEmailAsync(user, dto.Email);
                return Ok(new { status = 200, message = "If the email is registered and unconfirmed, a confirmation link has been sent." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resend confirmation email to {Email}", dto.Email);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    status = StatusCodes.Status503ServiceUnavailable,
                    message = "The confirmation email could not be sent. Please try again later."
                });
            }
        }

        private string ResolveFrontendBaseUrl()
        {
            var origin = Request.Headers.Origin.ToString().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(origin))
            {
                return origin;
            }

            return (_configuration["App:FrontendBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        }

        private async Task<string> BuildConfirmationLinkAsync(IdentityUser user, string email)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebUtility.UrlEncode(token);
            var encodedEmail = WebUtility.UrlEncode(email);
            var frontendBase = ResolveFrontendBaseUrl();
            return $"{frontendBase}/confirm-email?email={encodedEmail}&token={encodedToken}";
        }

        private async Task SendConfirmationEmailAsync(IdentityUser user, string email)
        {
            var confirmLink = await BuildConfirmationLinkAsync(user, email);
            var body = EmailTemplates.BuildAccountConfirmationEmail(confirmLink);
            await _emailService.SendEmailAsync(email, "Confirm your email address", body);
        }

        // GET: api/auth/check
        // Basic auth check: returns 200 when the JWT is valid.
        [Authorize]
        [HttpGet("check")]
        public IActionResult CheckAuth()
        {
            return Ok(new
            {
                status = 200,
                message = "Authenticated.",
                data = new
                {
                    userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    email = User.FindFirst(ClaimTypes.Email)?.Value
                }
            });
        }
    }
}
