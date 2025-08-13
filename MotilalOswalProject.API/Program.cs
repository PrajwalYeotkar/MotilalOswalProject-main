using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<NotesDbContext>(opt => opt.UseInMemoryDatabase("NotesDb"));

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();
var notes = app.MapGroup("/notes");

// POST /notes → Create (201)
notes.MapPost("/", async ([FromBody] CreateNoteDto dto, NotesDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    var ctx = new ValidationContext(dto);
    if (!Validator.TryValidateObject(dto, ctx, validationResults, true))
    {
        return Results.ValidationProblem(validationResults
            .GroupBy(v => v.MemberNames.FirstOrDefault() ?? "")
            .ToDictionary(g => g.Key, g => g.Select(v => v.ErrorMessage!).ToArray()));
    }

    var note = new Note
    {
        Title = dto.Title.Trim(),
        Content = string.IsNullOrWhiteSpace(dto.Content) ? null : dto.Content,
        CreatedAt = DateTime.UtcNow
    };

    db.Notes.Add(note);
    await db.SaveChangesAsync();

    return Results.Created($"/notes/{note.Id}", note);
})
.Produces<Note>(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.WithName("CreateNote");

// GET /notes → List all (200), optional filter ?q=
notes.MapGet("/", async ([FromQuery] string? q, NotesDbContext db) =>
{
    IQueryable<Note> query = db.Notes.AsQueryable();

    if (!string.IsNullOrWhiteSpace(q))
    {
        var keyword = q.Trim();
        query = query.Where(n =>
            EF.Functions.Like(n.Title, $"%{keyword}%") ||
            (n.Content != null && EF.Functions.Like(n.Content, $"%{keyword}%")));
    }

    var list = await query
        .OrderByDescending(n => n.CreatedAt)
        .ToListAsync();

    return Results.Ok(list);
})
.Produces<List<Note>>(StatusCodes.Status200OK)
.WithName("GetNotes");

// GET /notes/{id} → Get by ID (200/404)
notes.MapGet("/{id:int}", async (int id, NotesDbContext db) =>
{
    var note = await db.Notes.FindAsync(id);
    return note is null
        ? Results.NotFound(new { message = "Note not found." })
        : Results.Ok(note);
})
.Produces<Note>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName("GetNoteById");

// PUT /notes/{id} → Update (200/404/400)
notes.MapPut("/{id:int}", async (int id, [FromBody] UpdateNoteDto dto, NotesDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    var ctx = new ValidationContext(dto);
    if (!Validator.TryValidateObject(dto, ctx, validationResults, true))
    {
        return Results.ValidationProblem(validationResults
            .GroupBy(v => v.MemberNames.FirstOrDefault() ?? "")
            .ToDictionary(g => g.Key, g => g.Select(v => v.ErrorMessage!).ToArray()));
    }

    var note = await db.Notes.FindAsync(id);
    if (note is null)
        return Results.NotFound(new { message = "Note not found." });

    note.Title = dto.Title.Trim();
    note.Content = string.IsNullOrWhiteSpace(dto.Content) ? null : dto.Content;

    await db.SaveChangesAsync();
    return Results.Ok(note);
})
.Produces<Note>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status400BadRequest)
.WithName("UpdateNote");

// DELETE /notes/{id} → Delete (200/404)
notes.MapDelete("/{id:int}", async (int id, NotesDbContext db) =>
{
    var note = await db.Notes.FindAsync(id);
    if (note is null)
        return Results.NotFound(new { message = "Note not found." });

    db.Notes.Remove(note);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Note deleted successfully." });
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName("DeleteNote");

// Redirect root to Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

// DTOs
record CreateNoteDto(
    [property: Required, StringLength(100)] string Title,
    string? Content
);

record UpdateNoteDto(
    [property: Required, StringLength(100)] string Title,
    string? Content
);

// Entity
class Note
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Title { get; set; } = default!;

    public string? Content { get; set; }

    public DateTime CreatedAt { get; set; }
}

// DbContext
class NotesDbContext : DbContext
{
    public NotesDbContext(DbContextOptions<NotesDbContext> options) : base(options) { }
    public DbSet<Note> Notes => Set<Note>();
}