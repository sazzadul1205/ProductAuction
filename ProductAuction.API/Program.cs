using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProductAuction.API.Data;
using ProductAuction.API.Data.Seeder;
using ProductAuction.API.Services;
using ProductAuction.API.Services.Background;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure ASP.NET Core Identity
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Read JWT settings from appsettings.json.
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;

// Configure JWT token validation.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddOpenApi();

// Register Services
builder.Services.AddScoped<IImageUploadService, ImageUploadService>();

// Background service that activates auctions when their scheduled start time arrives.
builder.Services.AddHostedService<AuctionStatusBackgroundService>();

// Background service that processes ended auctions, determines winners,
// and creates sale records when a winning bid exists.
builder.Services.AddHostedService<AuctionResultBackgroundService>();

// Background service that marks unpaid sales as overdue.
builder.Services.AddHostedService<PaymentOverdueBackgroundService>();

// Background service that marks deliveries as overdue when sellers miss their deadlines.
builder.Services.AddHostedService<DispatchOverdueBackgroundService>();

var app = builder.Build();

// Create the default roles when the API starts.
await RoleSeeder.SeedRolesAsync(app.Services);

// Create the admin account when the API starts.
await AdminAccountSeeder.SeedAdminAccountAsync(app.Services);

// Create the platform account and wallet when the API starts.
await PlatformAccountSeeder.SeedPlatformAccountAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// To Server Static Files Like images
app.UseStaticFiles();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();