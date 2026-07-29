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
    
    [HttpGet("/task")]
    public async Task<IActionResult> Index(Guid? id)
    { 
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string projectByIdSql = "SELECT \"PROJECT_ID\" AS Id, \"USER_ID\" AS UserId, \"NAME\", \"DESCRIPTION\", \"CREATED_DATE\" AS CreatedAt, \"UPDATED_DATE\" AS UpdatedAt FROM \"MM_PROJECT\" WHERE \"PROJECT_ID\" = @Id;";
        const string firstProjectSql = "SELECT TOP 1 \"PROJECT_ID\" AS Id, \"USER_ID\" AS UserId, \"NAME\", \"DESCRIPTION\", \"CREATED_DATE\" AS CreatedAt, \"UPDATED_DATE\" AS UpdatedAt FROM \"MM_PROJECT\" ORDER BY \"CREATED_DATE\";";

        var project = id.HasValue
            ? await connection.QuerySingleOrDefaultAsync<Project>(projectByIdSql, new { Id = id.Value })
            : await connection.QuerySingleOrDefaultAsync<Project>(firstProjectSql);

        if (project == null)
        {
            return View((Project?)null);
        }

        project.Columns = (await connection.QueryAsync<ProjectColumn>(
            "SELECT \"PROJECT_COLUMN_ID\" AS Id, \"PROJECT_ID\" AS ProjectId, \"NAME\", \"POSITION\", \"CREATED_DATE\" AS CreatedAt FROM \"DE_PROJECT_COLUMN\" WHERE \"PROJECT_ID\" = @ProjectId ORDER BY \"POSITION\";",
            new { ProjectId = project.Id })).ToList();

        project.Tags = (await connection.QueryAsync<ProjectTag>(
            "SELECT \"PROJECT_TAG_ID\" AS Id, \"PROJECT_ID\" AS ProjectId, \"NAME\", \"COLOR\", \"CREATED_DATE\" AS CreatedAt FROM \"MM_PROJECT_TAG\" WHERE \"PROJECT_ID\" = @ProjectId;",
            new { ProjectId = project.Id })).ToList();

        project.Collaborators = (await connection.QueryAsync<ProjectCollaborator, User, ProjectCollaborator>(
            "SELECT PC.\"PROJECT_ID\" AS ProjectId, PC.\"USER_ID\" AS UserId, PC.\"ROLE\" AS Role, PC.\"CREATED_DATE\" AS JoinedAt, U.\"USER_ID\" AS Id, U.\"FULL_NAME\" AS FullName, U.\"EMAIL\" AS Email, U.\"CREATED_DATE\" AS CreatedAt FROM \"DE_PROJECT_COLLABORATOR\" PC JOIN \"MM_USER\" U ON U.\"USER_ID\" = PC.\"USER_ID\" WHERE PC.\"PROJECT_ID\" = @ProjectId;",
            (collaborator, user) =>
            {
                collaborator.User = user;
                return collaborator;
            },
            new { ProjectId = project.Id },
            splitOn: "Id")).ToList();

        project.Tasks = (await connection.QueryAsync<TaskItem>(
            "SELECT \"TASK_ITEM_ID\" AS Id, \"TITLE\", \"DESCRIPTION\", \"PROJECT_ID\" AS ProjectId, \"PROJECT_COLUMN_ID\" AS ColumnId, \"ASSIGNED_USER_ID\" AS AssignedUserId, \"START_DATE\" AS StartDate, \"DUE_DATE\" AS DueDate, \"PRIORITY\", \"CREATED_DATE\" AS CreatedAt, \"UPDATED_DATE\" AS UpdatedAt FROM \"DE_TASK_ITEM\" WHERE \"PROJECT_ID\" = @ProjectId;",
            new { ProjectId = project.Id })).ToList();

        return View(project);
    }

    [HttpGet("/task/item")]
    public async Task<IActionResult> Item()
    {
        return View();
    }

    [HttpGet("/task/detail")]
    public IActionResult Detail(Guid projectId, Guid taskId)
    {
        if (projectId == Guid.Empty || taskId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ProjectId = projectId;
        ViewBag.TaskId = taskId;
        return View();
    }
}