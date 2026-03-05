using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TodoApi;

var builder = WebApplication.CreateBuilder(args);

// Add logging
var logger = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
}).CreateLogger("TodoApiLogger");
logger.LogInformation("Starting TodoApi backend...");
Console.WriteLine("[Startup] TodoApi backend is starting...");
builder.Services.AddDbContext<ToDoDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("ToDoDB"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("ToDoDB"))
    )
);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

// הוספת DbContext
builder.Services.AddDbContext<ToDoDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("ToDoDB");
    logger.LogInformation($"[DB] Using connection string: {connStr}");
    Console.WriteLine($"[DB] Using connection string: {connStr}");
    options.UseMySql(connStr, ServerVersion.AutoDetect(connStr));
});

// JWT Configuration
var jwtKey = "your-super-secret-jwt-key-that-is-at-least-32-characters-long!";
var jwtIssuer = "TodoApi";
var jwtAudience = "TodoApiUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors("AllowAll");
logger.LogInformation("CORS policy 'AllowAll' enabled.");
Console.WriteLine("[Startup] CORS policy 'AllowAll' enabled.");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
logger.LogInformation("Authentication and Authorization middleware enabled.");
Console.WriteLine("[Startup] Authentication and Authorization enabled.");

// Helper method to generate JWT token
string GenerateJwtToken(User user)
{
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes(jwtKey);
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        }),
        Expires = DateTime.UtcNow.AddDays(7),
        Issuer = jwtIssuer,
        Audience = jwtAudience,
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

// Authentication routes
app.MapPost("/auth/register", async (ToDoDbContext db, User newUser) =>
{
    logger.LogInformation($"[Register] Attempt to register user: {newUser.Username}");
    Console.WriteLine($"[Register] Attempt to register user: {newUser.Username}");
    // Check if user already exists
    var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == newUser.Username);
logger.LogDebug($"[Register] User exists: {existingUser != null}");
    if (existingUser != null)
    {
        logger.LogWarning($"[Register] Username already exists: {newUser.Username}");
Console.WriteLine($"[Register] Username already exists: {newUser.Username}");
return Results.BadRequest(new { message = "Username already exists" });
    }

    // Hash password (in production, use proper password hashing like BCrypt)
    logger.LogDebug($"[Register] Hashing password for user: {newUser.Username}");
newUser.Password = BCrypt.Net.BCrypt.HashPassword(newUser.Password);
    
    db.Users.Add(newUser);
logger.LogInformation($"[Register] Added user: {newUser.Username}");
await db.SaveChangesAsync();
logger.LogInformation($"[Register] Saved user: {newUser.Username}");
    
    var token = GenerateJwtToken(newUser);
logger.LogInformation($"[Register] JWT token generated for user: {newUser.Username}");
    logger.LogInformation($"[Register] Registration successful for user: {newUser.Username}");
Console.WriteLine($"[Register] Registration successful for user: {newUser.Username}");
return Results.Ok(new { token, user = new { newUser.Id, newUser.Username } });
});

app.MapPost("/auth/login", async (ToDoDbContext db, User loginUser) =>
{
    logger.LogInformation($"[Login] Attempt to login user: {loginUser.Username}");
    Console.WriteLine($"[Login] Attempt to login user: {loginUser.Username}");
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == loginUser.Username);
logger.LogDebug($"[Login] User found: {user != null}");
    if (user == null || !BCrypt.Net.BCrypt.Verify(loginUser.Password, user.Password))
{
    logger.LogWarning($"[Login] Invalid credentials for user: {loginUser.Username}");
    Console.WriteLine($"[Login] Invalid credentials for user: {loginUser.Username}");
    return Results.Unauthorized();
}

var token = GenerateJwtToken(user);
logger.LogInformation($"[Login] JWT token generated for user: {loginUser.Username}");
logger.LogInformation($"[Login] Login successful for user: {loginUser.Username}");
Console.WriteLine($"[Login] Login successful for user: {loginUser.Username}");
return Results.Ok(new { token, user = new { user.Id, user.Username } });
});


// הגדרת ה-routes
app.MapGet("/items", async (ToDoDbContext db) =>
{
    logger.LogInformation("[Items] Fetching all items");
    Console.WriteLine("[Items] Fetching all items");
    var items = await db.Items.ToListAsync();
    logger.LogDebug($"[Items] Fetched {items.Count} items");
    return items; // שליפת כל המשימות
}).RequireAuthorization();

app.MapPost("/items", async (ToDoDbContext db, Item item) =>
{
    logger.LogInformation($"[Items] Adding new item: {item.Name}");
    Console.WriteLine($"[Items] Adding new item: {item.Name}");
    db.Items.Add(item); // הוספת משימה חדשה
    await db.SaveChangesAsync();
    logger.LogInformation($"[Items] Added item with id: {item.Id}");
    return Results.Created($"/items/{item.Id}", item);
}).RequireAuthorization();

app.MapPut("/items/{id}", async (int id, ToDoDbContext db, Item updatedItem) =>
{
    logger.LogInformation($"[Items] Updating item id: {id}");
    Console.WriteLine($"[Items] Updating item id: {id}");
    var item = await db.Items.FindAsync(id);
    if (item is null)
    {
        logger.LogWarning($"[Items] Item not found for update: {id}");
        Console.WriteLine($"[Items] Item not found for update: {id}");
        return Results.NotFound();
    }

    item.Name = updatedItem.Name; // עדכון משימה
    item.IsComplete = updatedItem.IsComplete;
    logger.LogDebug($"[Items] Updated item: {item.Name}, IsComplete: {item.IsComplete}");
    await db.SaveChangesAsync();
    logger.LogInformation($"[Items] Updated item id: {id}");
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/items/{id}", async (int id, ToDoDbContext db) =>
{
    logger.LogInformation($"[Items] Deleting item id: {id}");
    Console.WriteLine($"[Items] Deleting item id: {id}");
    var item = await db.Items.FindAsync(id);
    if (item is null)
    {
        logger.LogWarning($"[Items] Item not found for delete: {id}");
        Console.WriteLine($"[Items] Item not found for delete: {id}");
        return Results.NotFound();
    }

    db.Items.Remove(item); // מחיקת משימה
    await db.SaveChangesAsync();
    logger.LogInformation($"[Items] Deleted item id: {id}");
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/health", async (ToDoDbContext db) => {
    logger.LogInformation("[Health] Health check pinged");
    string dbStatus;
    try
    {
        // Try to connect and run a simple query
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT 1;");
        dbStatus = "Healthy";
        logger.LogInformation("[Health] MySQL connectivity: Healthy");
    }
    catch (Exception ex)
    {
        dbStatus = $"Unhealthy: {ex.Message}";
        logger.LogError($"[Health] MySQL connectivity error: {ex.Message}");
    }
    finally
    {
        try { await db.Database.CloseConnectionAsync(); } catch {}
    }
    return Results.Ok(new { status = "Healthy", dbStatus, time = DateTime.UtcNow });
});

app.MapGet("/", () => Results.Ok(new { status = "ok", message = "Todo API is running!" }));

app.Run();



