using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CMS.Controllers;

[Authorize]
public class ProjectController : Controller
{
    private readonly DapperContext _context;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(DapperContext context, ILogger<ProjectController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Project project, [FromForm] Guid[]? collaboratorIds)
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(userIdValue, out var ownerUserId))
        {
            return Unauthorized();
        }

        if (project.Columns.Count == 0)
        {
            ModelState.AddModelError(nameof(project.Columns), "Add at least one column.");
        }

        if (project.Tags.Count == 0)
        {
            ModelState.AddModelError(nameof(project.Tags), "Add at least one tag.");
        }

        var selectedCollaboratorIds = (collaboratorIds ?? Array.Empty<Guid>()).Distinct().ToArray();
        await using var connection = await _context.CreateOpenConnectionAsync();

        var validCollaboratorIds = new List<Guid>();
        if (selectedCollaboratorIds.Length > 0)
        {
            validCollaboratorIds = (await connection.QueryAsync<Guid>(
                "SELECT \"USER_ID\" FROM \"MM_USER\" WHERE \"USER_ID\" = ANY(@Ids);",
                new { Ids = selectedCollaboratorIds }))
                .ToList();

            if (validCollaboratorIds.Count != selectedCollaboratorIds.Length)
            {
                ModelState.AddModelError(nameof(collaboratorIds), "One or more selected collaborators could not be found.");
            }
        }

        var projectToSave = new Project
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
            Name = project.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(project.Description)
                ? null
                : project.Description.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Columns = project.Columns
                .Select((column, index) => new ProjectColumn
                {
                    Id = Guid.NewGuid(),
                    ProjectId = Guid.Empty,
                    Name = column.Name.Trim(),
                    Position = index,
                    CreatedAt = DateTime.UtcNow,
                })
                .ToList(),
            Tags = project.Tags
                .Select(tag => new ProjectTag
                {
                    Id = Guid.NewGuid(),
                    ProjectId = Guid.Empty,
                    Name = tag.Name.Trim(),
                    Color = string.IsNullOrWhiteSpace(tag.Color) ? null : tag.Color,
                    CreatedAt = DateTime.UtcNow,
                })
                .ToList(),
            Collaborators = validCollaboratorIds
                .Where(userId => userId != ownerUserId)
                .Select(userId => new ProjectCollaborator
                {
                    ProjectId = Guid.Empty,
                    UserId = userId,
                    Role = "member",
                    JoinedAt = DateTime.UtcNow,
                })
                .ToList(),
        };

        projectToSave.Collaborators.Add(new ProjectCollaborator
        {
            ProjectId = Guid.Empty,
            UserId = ownerUserId,
            Role = "owner",
            JoinedAt = DateTime.UtcNow,
        });

        foreach (var column in projectToSave.Columns)
        {
            column.Project = projectToSave;
        }

        foreach (var tag in projectToSave.Tags)
        {
            tag.Project = projectToSave;
        }

        foreach (var collaborator in projectToSave.Collaborators)
        {
            collaborator.Project = projectToSave;
        }

        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string insertProjectSql = "INSERT INTO \"MM_PROJECT\" (\"PROJECT_ID\", \"USER_ID\", \"NAME\", \"DESCRIPTION\", \"RECORD_TYP\", \"CREATED_BY\", \"CREATED_DATE\", \"CREATED_LOC\", \"UPDATED_BY\", \"UPDATED_DATE\", \"UPDATED_LOC\") VALUES (@Id, @UserId, @Name, @Description, 1, 'SYSTEM', @CreatedAt, '127.0.0.1', 'SYSTEM', @UpdatedAt, '127.0.0.1');";
            await connection.ExecuteAsync(insertProjectSql, projectToSave, transaction);

            const string insertColumnSql = "INSERT INTO \"DE_PROJECT_COLUMN\" (\"PROJECT_COLUMN_ID\", \"PROJECT_ID\", \"NAME\", \"POSITION\", \"RECORD_TYP\", \"CREATED_BY\", \"CREATED_DATE\", \"CREATED_LOC\") VALUES (@Id, @ProjectId, @Name, @Position, 1, 'SYSTEM', @CreatedAt, '127.0.0.1');";
            foreach (var column in projectToSave.Columns)
            {
                column.ProjectId = projectToSave.Id;
                await connection.ExecuteAsync(insertColumnSql, column, transaction);
            }

            const string insertTagSql = "INSERT INTO \"MM_PROJECT_TAG\" (\"PROJECT_TAG_ID\", \"PROJECT_ID\", \"NAME\", \"COLOR\", \"RECORD_TYP\", \"CREATED_BY\", \"CREATED_DATE\", \"CREATED_LOC\") VALUES (@Id, @ProjectId, @Name, @Color, 1, 'SYSTEM', @CreatedAt, '127.0.0.1');";
            foreach (var tag in projectToSave.Tags)
            {
                tag.ProjectId = projectToSave.Id;
                await connection.ExecuteAsync(insertTagSql, tag, transaction);
            }

            const string insertCollaboratorSql = "INSERT INTO \"DE_PROJECT_COLLABORATOR\" (\"PROJECT_ID\", \"USER_ID\", \"ROLE\", \"RECORD_TYP\", \"CREATED_BY\", \"CREATED_DATE\", \"CREATED_LOC\") VALUES (@ProjectId, @UserId, @Role, 1, 'SYSTEM', @JoinedAt, '127.0.0.1');";
            foreach (var collaborator in projectToSave.Collaborators)
            {
                collaborator.ProjectId = projectToSave.Id;
                await connection.ExecuteAsync(insertCollaboratorSql, collaborator, transaction);
            }

            await transaction.CommitAsync();
            Console.WriteLine($"Project insert succeeded. ProjectId={projectToSave.Id}, Columns={projectToSave.Columns.Count}, Tags={projectToSave.Tags.Count}, Collaborators={projectToSave.Collaborators.Count}");
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception rollbackException)
            {
                _logger.LogWarning(rollbackException, "Rollback failed for project creation transaction.");
            }

            _logger.LogError(exception, "Project creation failed for project name {ProjectName}", project.Name);
            Console.Error.WriteLine($"Project creation failed. Name={project.Name}, Columns={project.Columns.Count}, Tags={project.Tags.Count}, Collaborators={selectedCollaboratorIds.Length}");
            Console.Error.WriteLine(exception.ToString());
            var errorMessage = exception.GetBaseException().Message;
            TempData["ToastMessage"] = $"The project could not be saved: {errorMessage}";
            TempData["ToastType"] = "danger";
            return RedirectToAction("Index", "Task");
        }

        return RedirectToAction("Index", "Task", new { id = projectToSave.Id });
    }

    private static Project CreateDefaultProject()
    {
        var project = new Project();

        project.Columns.Add(new ProjectColumn { Name = "TO DO", Position = 0 });
        project.Columns.Add(new ProjectColumn { Name = "IN PROGRESS", Position = 1 });
        project.Columns.Add(new ProjectColumn { Name = "PENDING REVIEW", Position = 2 });
        project.Columns.Add(new ProjectColumn { Name = "PENDING TO DEPLOY", Position = 3 });
        project.Columns.Add(new ProjectColumn { Name = "COMPLETED", Position = 4 });

        return project;
    }
}