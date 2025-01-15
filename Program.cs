using Microsoft.EntityFrameworkCore;
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// שימוש במדיניות ה-CORS הפתוחה
app.UseCors("AllowAll");

// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
// }
app.MapGet("/", () => "Welcome to Todo API!");

// Get all items
app.MapGet("/Items", async (ToDoDbContext db) =>
    await db.Items.ToListAsync());

// Get item by ID
app.MapGet("/Item/{id}", async (int id, ToDoDbContext db) =>
    await db.Items.FindAsync(id) is Item item
        ? Results.Ok(item)
        : Results.NotFound());

// Add new item
app.MapPost("/Item", async (Item newItem, ToDoDbContext db) =>
{
    newItem.IsComplete = false; // ברירת מחדל
    db.Items.Add(newItem);
    await db.SaveChangesAsync();
    return Results.Created($"/Item/{newItem.Id}", newItem);
});

// Update item
app.MapPut("/Item/{id}", async (int id, bool isComplete, ToDoDbContext db) =>
{
    var item = await db.Items.FindAsync(id);

    if (item is null) return Results.NotFound();

    item.IsComplete = isComplete;

    await db.SaveChangesAsync();

    return Results.Ok(item); // מחזיר את הפריט המעודכן
});

// Delete item
app.MapDelete("/Item/{id}", async (int id, ToDoDbContext db) =>
{
    var item = await db.Items.FindAsync(id);

    if (item is null) return Results.NotFound();

    db.Items.Remove(item);
    await db.SaveChangesAsync();

    return Results.Ok(item);
});

app.Run();


