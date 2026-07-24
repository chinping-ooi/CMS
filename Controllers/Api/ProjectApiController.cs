using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectApiController : ControllerBase
{
    private readonly DapperContext _context;

    public ProjectApiController(DapperContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectApiResponse>>> GetAll()
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = "SELECT id, name, description, created_at, updated_at FROM project";

        var projects = (await connection.QueryAsync<Project>(sql)).ToList();
        return Ok(projects.Select(MapProject).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectApiResponse>> Get(Guid id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string projectSql = "SELECT id, name, description, created_at, updated_at FROM project WHERE id = @Id";

        var project = await connection.QuerySingleOrDefaultAsync<Project>(projectSql, new { Id = id });
        if (project == null)
        {
            return NotFound();
        }

        const string columnsSql = "SELECT id, project_id, name, position, created_at FROM project_column WHERE project_id = @ProjectId";

        project.Columns = (await connection.QueryAsync<ProjectColumn>(columnsSql, new { ProjectId = id })).ToList();

        const string tagsSql = "SELECT id, project_id, name, color, created_at FROM project_tag WHERE project_id = @ProjectId";

        project.Tags = (await connection.QueryAsync<ProjectTag>(tagsSql, new { ProjectId = id })).ToList();

        const string collaboratorsSql = "SELECT pc.project_id, pc.user_id, pc.role, pc.joined_at, u.id, u.full_name, u.email, u.created_at FROM project_collaborator pc LEFT JOIN users u ON u.id = pc.user_id WHERE pc.project_id = @ProjectId";

        project.Collaborators = (await connection.QueryAsync<ProjectCollaborator, User, ProjectCollaborator>(
            collaboratorsSql,
            (collaborator, user) =>
            {
                collaborator.User = user;
                return collaborator;
            },
            new { ProjectId = id },
            splitOn: "id")).ToList();

        const string tasksSql = "SELECT ti.id, ti.title, ti.description, ti.project_id, ti.column_id, ti.assigned_user_id, ti.due_date, ti.priority, ti.created_at, ti.updated_at, u.id, u.full_name, u.email, u.created_at FROM task_item ti LEFT JOIN users u ON u.id = ti.assigned_user_id WHERE ti.project_id = @ProjectId";

        project.Tasks = (await connection.QueryAsync<TaskItem, User, TaskItem>(
            tasksSql,
            (task, user) =>
            {
                task.AssignedUser = user;
                return task;
            },
            new { ProjectId = id },
            splitOn: "id")).ToList();

        if (project.Tasks.Any())
        {
            var taskIds = project.Tasks.Select(task => task.Id).ToArray();
            const string taskTagsSql = @"
                SELECT tit.task_id AS TaskId,
                       tit.tag_id AS TagId,
                       pt.id AS Id,
                       pt.name AS Name,
                       pt.color AS Color
                FROM task_item_tag tit
                JOIN project_tag pt ON pt.id = tit.tag_id
                WHERE tit.task_id = ANY(@TaskIds)";

            var taskTagRows = (await connection.QueryAsync<TaskTagRow>(taskTagsSql, new { TaskIds = taskIds })).ToList();
            var taskTagsByTask = taskTagRows.GroupBy(row => row.TaskId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var task in project.Tasks)
            {
                if (taskTagsByTask.TryGetValue(task.Id, out var rows))
                {
                    task.Tags = rows
                        .Select(row => new TaskItemTag
                        {
                            Task = task,
                            TaskId = task.Id,
                            TagId = row.TagId,
                            Tag = new ProjectTag
                            {
                                Id = row.Id,
                                Name = row.Name,
                                Color = row.Color,
                            },
                        })
                        .ToList();
                }
            }
        }

        // Load attachments for tasks
        if (project.Tasks.Any())
        {
            var taskIds = project.Tasks.Select(task => task.Id).ToArray();
            const string taskAttachmentsSql = @"
                SELECT id, task_id AS TaskId, file_name AS FileName, file_path AS FilePath, file_type AS FileType, file_size AS FileSize, uploaded_at AS UploadedAt
                FROM task_attachment
                WHERE task_id = ANY(@TaskIds)";

            var attachments = (await connection.QueryAsync<TaskAttachment>(taskAttachmentsSql, new { TaskIds = taskIds })).ToList();
            var attachmentsByTask = attachments.GroupBy(a => a.TaskId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var task in project.Tasks)
            {
                if (attachmentsByTask.TryGetValue(task.Id, out var att))
                {
                    task.Attachments = att;
                }
            }
        }

        return Ok(MapProject(project));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectApiResponse>> Create(Project project)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        project.Id = project.Id == Guid.Empty ? Guid.NewGuid() : project.Id;
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        await using var connection = await _context.CreateOpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string insertProjectSql = "INSERT INTO project (id, name, description, created_at, updated_at) VALUES (@Id, @Name, @Description, @CreatedAt, @UpdatedAt)";

        await connection.ExecuteAsync(insertProjectSql, project, transaction);

        const string insertColumnSql = "INSERT INTO project_column (id, project_id, name, position, created_at) VALUES (@Id, @ProjectId, @Name, @Position, @CreatedAt)";

        foreach (var column in project.Columns)
        {
            column.Id = column.Id == Guid.Empty ? Guid.NewGuid() : column.Id;
            column.ProjectId = project.Id;
            column.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(insertColumnSql, column, transaction);
        }

        const string insertTagSql = "INSERT INTO project_tag (id, project_id, name, color, created_at) VALUES (@Id, @ProjectId, @Name, @Color, @CreatedAt)";

        foreach (var tag in project.Tags)
        {
            tag.Id = tag.Id == Guid.Empty ? Guid.NewGuid() : tag.Id;
            tag.ProjectId = project.Id;
            tag.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(insertTagSql, tag, transaction);
        }

        const string insertCollaboratorSql = "INSERT INTO project_collaborator (project_id, user_id, role, joined_at) VALUES (@ProjectId, @UserId, @Role, @JoinedAt)";

        foreach (var collaborator in project.Collaborators)
        {
            collaborator.ProjectId = project.Id;
            collaborator.JoinedAt = collaborator.JoinedAt == default ? DateTime.UtcNow : collaborator.JoinedAt;
            await connection.ExecuteAsync(insertCollaboratorSql, collaborator, transaction);
        }

        await transaction.CommitAsync();

        return CreatedAtAction(nameof(Get), new { id = project.Id }, MapProject(project));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Project project)
    {
        if (id != project.Id)
        {
            return BadRequest();
        }

        project.UpdatedAt = DateTime.UtcNow;

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string updateSql = "UPDATE project SET name = @Name, description = @Description, updated_at = @UpdatedAt WHERE id = @Id";

        var rows = await connection.ExecuteAsync(updateSql, new
        {
            project.Name,
            project.Description,
            project.UpdatedAt,
            Id = id,
        });

        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string deleteSql = "DELETE FROM project WHERE id = @Id";

        var rows = await connection.ExecuteAsync(deleteSql, new { Id = id });
        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/collaborators")]
    public async Task<IActionResult> AddCollaborator(Guid id, AddProjectCollaboratorRequest request)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string projectExistsSql = "SELECT COUNT(1) > 0 FROM project WHERE id = @Id";
        var projectExists = await connection.ExecuteScalarAsync<bool>(projectExistsSql, new { Id = id });
        if (!projectExists)
        {
            return NotFound();
        }

        const string userExistsSql = "SELECT COUNT(1) > 0 FROM users WHERE id = @UserId";
        var userExists = await connection.ExecuteScalarAsync<bool>(userExistsSql, new { request.UserId });
        if (!userExists)
        {
            return BadRequest("User not found.");
        }

        const string collaboratorExistsSql = "SELECT COUNT(1) FROM project_collaborator WHERE project_id = @ProjectId AND user_id = @UserId";
        var existingCollaborator = await connection.ExecuteScalarAsync<int>(collaboratorExistsSql, new { ProjectId = id, request.UserId });
        if (existingCollaborator > 0)
        {
            return Conflict("Collaborator already added.");
        }

        const string insertCollaboratorSql = "INSERT INTO project_collaborator (project_id, user_id, role, joined_at) VALUES (@ProjectId, @UserId, @Role, @JoinedAt)";
        var inserted = await connection.ExecuteAsync(insertCollaboratorSql, new { ProjectId = id, request.UserId, request.Role, JoinedAt = DateTime.UtcNow });
        if (inserted == 0)
        {
            return StatusCode(500);
        }

        return NoContent();
    }

    private static ProjectApiResponse MapProject(Project project)
    {
        return new ProjectApiResponse
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            Columns = project.Columns
                .OrderBy(column => column.Position)
                .Select(column => new ProjectColumnApiResponse
                {
                    Id = column.Id,
                    Name = column.Name,
                    Position = column.Position,
                })
                .ToList(),
            Tags = project.Tags
                .Select(tag => new ProjectTagApiResponse
                {
                    Id = tag.Id,
                    Name = tag.Name,
                    Color = tag.Color,
                })
                .ToList(),
            Collaborators = project.Collaborators
                .Select(collaborator => new ProjectCollaboratorApiResponse
                {
                    UserId = collaborator.UserId,
                    Role = collaborator.Role,
                    FullName = collaborator.User?.FullName ?? string.Empty,
                    Email = collaborator.User?.Email ?? string.Empty,
                })
                .ToList(),
            Tasks = project.Tasks
                .Select(task => new TaskItemApiResponse
                {
                    Id = task.Id,
                    Title = task.Title,
                    Description = task.Description,
                    ColumnId = task.ColumnId,
                    AssignedUserName = task.AssignedUser?.FullName,
                    DueDate = task.DueDate,
                    Priority = task.Priority,
                    Tags = task.Tags
                        .Select(taskTag => new ProjectTagApiResponse
                        {
                            Id = taskTag.Tag.Id,
                            Name = taskTag.Tag.Name,
                            Color = taskTag.Tag.Color,
                        })
                        .ToList(),
                })
                .ToList(),
        };
    }
}

public class ProjectApiResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ProjectColumnApiResponse> Columns { get; set; } = [];
    public List<ProjectTagApiResponse> Tags { get; set; } = [];
    public List<ProjectCollaboratorApiResponse> Collaborators { get; set; } = [];
    public List<TaskItemApiResponse> Tasks { get; set; } = [];
}

public class ProjectColumnApiResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
}

public class ProjectTagApiResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

public class ProjectCollaboratorApiResponse
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class TaskItemApiResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid ColumnId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public List<ProjectTagApiResponse> Tags { get; set; } = [];
}

public sealed record AddProjectCollaboratorRequest(Guid UserId, string Role = "member");

public sealed class TaskTagRow
{
    public Guid TaskId { get; init; }
    public Guid TagId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Color { get; init; }
}
