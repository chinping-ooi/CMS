using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers;

public class ProjectController : Controller
{
    private readonly DapperContext _context;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(DapperContext context, ILogger<ProjectController> logger)
    {
        _context = context;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View(CreateDefaultProject());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Project project, [FromForm] Guid[]? collaboratorIds)
    {
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
                "SELECT id FROM users WHERE id = ANY(@Ids)",
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
                .Select(userId => new ProjectCollaborator
                {
                    ProjectId = Guid.Empty,
                    UserId = userId,
                    Role = "member",
                    JoinedAt = DateTime.UtcNow,
                })
                .ToList(),
        };

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
            Console.WriteLine($"Starting project insert: {projectToSave.Name} ({projectToSave.Id})");

            const string insertProjectSql = "INSERT INTO project (id, name, description, created_at, updated_at) VALUES (@Id, @Name, @Description, @CreatedAt, @UpdatedAt)";
            await connection.ExecuteAsync(insertProjectSql, projectToSave, transaction);

            const string insertColumnSql = "INSERT INTO project_column (id, project_id, name, position, created_at) VALUES (@Id, @ProjectId, @Name, @Position, @CreatedAt)";
            foreach (var column in projectToSave.Columns)
            {
                column.ProjectId = projectToSave.Id;
                await connection.ExecuteAsync(insertColumnSql, column, transaction);
            }

            const string insertTagSql = "INSERT INTO project_tag (id, project_id, name, color, created_at) VALUES (@Id, @ProjectId, @Name, @Color, @CreatedAt)";
            foreach (var tag in projectToSave.Tags)
            {
                tag.ProjectId = projectToSave.Id;
                await connection.ExecuteAsync(insertTagSql, tag, transaction);
            }

            const string insertCollaboratorSql = "INSERT INTO project_collaborator (project_id, user_id, role, joined_at) VALUES (@ProjectId, @UserId, @Role, @JoinedAt)";
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
            ModelState.AddModelError(string.Empty, $"The project could not be saved: {errorMessage}");
            return View("Index", project);
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