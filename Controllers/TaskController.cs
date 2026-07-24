using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers;

[Route("[controller]/[action]")]
public class TaskController : Controller
{
    private readonly DapperContext _context;

    public TaskController(DapperContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(Guid? id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string projectByIdSql = "SELECT id, name, description, created_at AS CreatedAt, updated_at AS UpdatedAt FROM project WHERE id = @Id";
        const string firstProjectSql = "SELECT id, name, description, created_at AS CreatedAt, updated_at AS UpdatedAt FROM project ORDER BY created_at LIMIT 1";

        var project = id.HasValue
            ? await connection.QuerySingleOrDefaultAsync<Project>(projectByIdSql, new { Id = id.Value })
            : await connection.QuerySingleOrDefaultAsync<Project>(firstProjectSql);

        if (project == null)
        {
            return NotFound();
        }

        project.Columns = (await connection.QueryAsync<ProjectColumn>(
            "SELECT id, project_id AS ProjectId, name, position, created_at AS CreatedAt FROM project_column WHERE project_id = @ProjectId ORDER BY position",
            new { ProjectId = project.Id })).ToList();

        project.Tags = (await connection.QueryAsync<ProjectTag>(
            "SELECT id, project_id AS ProjectId, name, color, created_at AS CreatedAt FROM project_tag WHERE project_id = @ProjectId",
            new { ProjectId = project.Id })).ToList();

        project.Collaborators = (await connection.QueryAsync<ProjectCollaborator, User, ProjectCollaborator>(
            "SELECT pc.project_id AS ProjectId, pc.user_id AS UserId, pc.role AS Role, pc.joined_at AS JoinedAt, u.id AS Id, u.full_name AS FullName, u.email AS Email, u.created_at AS CreatedAt FROM project_collaborator pc JOIN users u ON u.id = pc.user_id WHERE pc.project_id = @ProjectId",
            (collaborator, user) =>
            {
                collaborator.User = user;
                return collaborator;
            },
            new { ProjectId = project.Id },
            splitOn: "Id")).ToList();

        project.Tasks = (await connection.QueryAsync<TaskItem>(
            "SELECT id, title, description, project_id AS ProjectId, column_id AS ColumnId, assigned_user_id AS AssignedUserId, due_date AS DueDate, priority, created_at AS CreatedAt, updated_at AS UpdatedAt FROM task_item WHERE project_id = @ProjectId",
            new { ProjectId = project.Id })).ToList();

        return View(project);
    }

    public async Task<IActionResult> Item()
    {
        return View();
    }
}