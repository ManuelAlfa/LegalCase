using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class UsuariosEndpoints
{
    public static WebApplication MapUsuariosEndpoints(this WebApplication app)
    {
        app.MapGet("/api/usuarios", async (AppDbContext db) =>
                await db.Usuarios
                    .OrderBy(u => u.Nombre)
                    .Select(u => new UsuarioResponse(u.Id, u.Nombre, u.Email, u.Rol))
                    .ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/usuarios/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.Usuarios
                    .Where(u => u.Id == id)
                    .Select(u => new UsuarioResponse(u.Id, u.Nombre, u.Email, u.Rol))
                    .FirstOrDefaultAsync() is { } usuario
                    ? Results.Ok(usuario)
                    : Results.NotFound())
            .RequireAuthorization();

        // PasswordHash se recibe ya calculado por el llamador: todavía no hay
        // un flujo de alta de usuario real que haga el hashing (bcrypt/argon2)
        // en el servidor; eso pertenece a una fase posterior de autenticación,
        // no a este CRUD de metadatos.
        app.MapPost("/api/usuarios", async (UsuarioCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                var usuario = new Usuario
                {
                    TenantId = tenant.TenantId,
                    Nombre = request.Nombre,
                    Email = request.Email,
                    PasswordHash = request.PasswordHash,
                    Rol = request.Rol
                };

                db.Usuarios.Add(usuario);
                await db.SaveChangesAsync();

                var response = new UsuarioResponse(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol);
                return Results.Created($"/api/usuarios/{usuario.Id}", response);
            })
            .RequireAuthorization();

        app.MapPatch("/api/usuarios/{id:guid}", async (Guid id, UsuarioUpdateRequest request, AppDbContext db) =>
            {
                var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
                if (usuario is null)
                    return Results.NotFound();

                if (request.Nombre is not null) usuario.Nombre = request.Nombre;
                if (request.Email is not null) usuario.Email = request.Email;
                if (request.PasswordHash is not null) usuario.PasswordHash = request.PasswordHash;
                if (request.Rol is not null) usuario.Rol = request.Rol.Value;

                await db.SaveChangesAsync();

                return Results.Ok(new UsuarioResponse(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol));
            })
            .RequireAuthorization();

        return app;
    }
}

public record UsuarioCreateRequest(string Nombre, string Email, string PasswordHash, RolUsuario Rol);

public record UsuarioUpdateRequest(string? Nombre, string? Email, string? PasswordHash, RolUsuario? Rol);

// Nunca incluye PasswordHash: es lo único que se filtra deliberadamente de
// la entidad al serializar la respuesta.
public record UsuarioResponse(Guid Id, string Nombre, string Email, RolUsuario Rol);
