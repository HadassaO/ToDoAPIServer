using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TodoApi;

var builder = WebApplication.CreateBuilder(args);

// הוספת שירותי CORS עם מדיניות פתוחה
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin() // מתיר כל מקור
              .AllowAnyHeader() // מתיר כל כותרת
              .AllowAnyMethod(); // מתיר כל שיטת HTTP (GET, POST, וכו')
    });
});

builder.Services.AddDbContext<ToDoDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("ToDoDB"),
        Microsoft.EntityFrameworkCore.ServerVersion.Parse("8.0.40-mysql")
));

// added for token 
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "Bearer Authentication with JWT Token",
        Type = SecuritySchemeType.Http
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Id = "Bearer",
                    Type = ReferenceType.SecurityScheme
                }
            },
            new List<string>()
        }
    });
});

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
            ValidIssuer = builder.Configuration["JWT:Issuer"],
            ValidAudience = builder.Configuration["JWT:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JWT:Key"]))
        };
    });

var app = builder.Build();

// פונקציה ליצירת טוקן JWT
string GenerateJwtToken(string username, string email, int userId)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.Email, email),
        new Claim(ClaimTypes.Role, "User"),
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()) // הוספת מזהה המשתמש
    };
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.GetValue<string>("Key")));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtSettings.GetValue<string>("Issuer"),
        audience: jwtSettings.GetValue<string>("Audience"),
        claims: claims,
        expires: DateTime.Now.AddMinutes(30),
        signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

// שימוש במדיניות ה-CORS הפתוחה
app.UseCors("AllowAll");


app.UseSwagger();
app.UseSwaggerUI();

// register
app.MapPost("/register", async (RegisterDto registerDto, ToDoDbContext context) =>
{
    if (await context.Users.AnyAsync(u => u.UserName == registerDto.UserName))
    {
        return Results.BadRequest("User already exists.");
    }

    context.Users.Add(new User { UserName = registerDto.UserName,Email=registerDto.Email, Password = registerDto.Password });
    await context.SaveChangesAsync();
    return Results.Ok("User registered successfully.");
});



// login and get token 
app.MapPost("/login", async (LoginDto LoginUser, ToDoDbContext context) =>
{
    var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == LoginUser.UserName && u.Password == LoginUser.Password);
    if (user == null)
    {
        return Results.Json(new { message = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var token = GenerateJwtToken(LoginUser.UserName,user.Email,user.Id);
    return Results.Ok(new { token });
});

app.MapGet("/", () => "Welcome to Todo API!");

// הצגת כל המשימות של המשתמש המחובר
app.MapGet("/Items", async (ClaimsPrincipal user,ToDoDbContext db) =>
{
    var userId = GetUserIdFromClaims(user);
    if (userId == null) return Results.Unauthorized();

    var tasks = await db.Items.Where(item => item.UserId == userId).ToListAsync();
    return Results.Ok(tasks);
}).RequireAuthorization();

// Get item by ID
app.MapGet("/Item/{id}", async (int id, ToDoDbContext db) =>
    await db.Items.FindAsync(id) is Item item
        ? Results.Ok(item)
        : Results.NotFound());

// Add new item
app.MapPost("/Item", async (ItemDto newItemDto,  ClaimsPrincipal user,ToDoDbContext db) =>
{
    var userId = GetUserIdFromClaims(user); // קח את ה- userId מה-Claims
    if (userId == null) return Results.Unauthorized();

    var newTask = new Item
    {
        Name = newItemDto.Name,
        IsComplete = newItemDto.IsComplete,
        UserId = userId.Value
    };  // הוסף את ה- userId למטלה    
    
    db.Items.Add(newTask);
    await db.SaveChangesAsync();
    return Results.Created($"/Item/{newTask.Id}", newTask);
}).RequireAuthorization();

// Update item
app.MapPut("/Item/{id}", async (int id, bool isComplete, ClaimsPrincipal user, ToDoDbContext db) =>
{
    var userId = GetUserIdFromClaims(user);
    if (userId == null) return Results.Unauthorized();

    var task = await db.Items.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);

    if (task == null) return Results.NotFound($"Task with ID {id} not found.");

    task.IsComplete = isComplete;
    await db.SaveChangesAsync();
    return Results.Ok(task); // מחזיר את הפריט המעודכן
});

// Delete item
app.MapDelete("/Item/{id}", async (int id, ClaimsPrincipal user,ToDoDbContext db) =>
{
    var userId = GetUserIdFromClaims(user);
    if (userId == null) return Results.Unauthorized();

    var task = await db.Items.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
    if (task == null) return Results.NotFound($"Task with ID {id} not found.");

    db.Items.Remove(task);
    await db.SaveChangesAsync();
    return Results.Ok(task);
}).RequireAuthorization();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

int? GetUserIdFromClaims(ClaimsPrincipal user)
{
    var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
    return userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId) ? userId : null;
}

app.Run();
