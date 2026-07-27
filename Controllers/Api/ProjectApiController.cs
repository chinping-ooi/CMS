using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CMS.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
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

        const string sql = "SELECT id, user_id AS UserId, name, description, created_at, updated_at FROM project";

        var projects = (await connection.QueryAsync<Project>(sql)).ToList();
        return Ok(projects.Select(MapProject).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectApiResponse>> Get(Guid id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string projectSql = "SELECT id, user_id AS UserId, name, description, created_at, updated_at FROM project WHERE id = @Id";

        var project = await connection.QuerySingleOrDefaultAsync<Project>(projectSql, new { Id = id });
        if (project == null)
        {
            return NotFound();
        }

        const string columnsSql = "SELECT id, project_id, name, position, created_at FROM project_column WHERE project_id = @ProjectId";

        project.Columns = (await connection.QueryAsync<ProjectColumn>(columnsSql, new { ProjectId = id })).ToList();

        const string tagsSql = "SELECT id, project_id, name, color, created_at FROM project_tag WHERE project_id = @ProjectId";

        project.Tags = (await connection.QueryAsync<ProjectTag>(tagsSql, new { ProjectId = id })).ToList();

        const string collaboratorsSql = "SELECT pc.project_id AS ProjectId, pc.user_id AS UserId, pc.role AS Role, pc.joined_at AS JoinedAt, u.id AS Id, u.full_name AS FullName, u.email AS Email, u.created_at AS CreatedAt FROM project_collaborator pc LEFT JOIN users u ON u.id = pc.user_id WHERE pc.project_id = @ProjectId";

        project.Collaborators = (await connection.QueryAsync<ProjectCollaborator, User, ProjectCollaborator>(
            collaboratorsSql,
            (collaborator, user) =>
            {
                collaborator.User = user;
                return collaborator;
            },
            new { ProjectId = id },
            splitOn: "id")).ToList();

        const string tasksSql = "SELECT ti.id, ti.title, ti.description, ti.project_id AS ProjectId, ti.column_id AS ColumnId, ti.assigned_user_id AS AssignedUserId, ti.start_date AS StartDate, ti.due_date AS DueDate, ti.priority, ti.created_at AS CreatedAt, ti.updated_at AS UpdatedAt, u.id, u.full_name, u.email, u.created_at FROM task_item ti LEFT JOIN users u ON u.id = ti.assigned_user_id WHERE ti.project_id = @ProjectId";

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
            const string assigneesSql = @"
                SELECT ta.task_id AS TaskId, ta.user_id AS UserId, ta.assigned_at AS AssignedAt,
                       u.id AS Id, u.full_name AS FullName, u.email AS Email, u.created_at AS CreatedAt
                FROM task_assignee ta
                JOIN users u ON u.id = ta.user_id
                WHERE ta.task_id = ANY(@TaskIds)
                ORDER BY ta.assigned_at";

            var assignees = await connection.QueryAsync<TaskAssignee, User, TaskAssignee>(
                assigneesSql,
                (assignee, user) =>
                {
                    assignee.User = user;
                    return assignee;
                },
                new { TaskIds = taskIds },
                splitOn: "Id");

            var assigneesByTask = assignees
                .GroupBy(assignee => assignee.TaskId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var task in project.Tasks)
            {
                task.Assignees = assigneesByTask.TryGetValue(task.Id, out var taskAssignees)
                    ? taskAssignees
                    : [];
            }
        }

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

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(userIdValue, out var ownerUserId))
        {
            return Unauthorized();
        }

        project.Id = project.Id == Guid.Empty ? Guid.NewGuid() : project.Id;
        project.UserId = ownerUserId;
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        await using var connection = await _context.CreateOpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string insertProjectSql = "INSERT INTO project (id, user_id, name, description, created_at, updated_at) VALUES (@Id, @UserId, @Name, @Description, @CreatedAt, @UpdatedAt)";

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

        var collaborators = project.Collaborators
            .Where(collaborator => collaborator.UserId != ownerUserId)
            .ToList();
        collaborators.Insert(0, new ProjectCollaborator
        {
            ProjectId = project.Id,
            UserId = ownerUserId,
            Role = "owner",
            JoinedAt = DateTime.UtcNow,
        });

        foreach (var collaborator in collaborators)
        {
            collaborator.ProjectId = project.Id;
            collaborator.JoinedAt = collaborator.JoinedAt == default ? DateTime.UtcNow : collaborator.JoinedAt;
            await connection.ExecuteAsync(insertCollaboratorSql, collaborator, transaction);
        }

        await transaction.CommitAsync();

        return CreatedAtAction(nameof(Get), new { id = project.Id }, MapProject(project));
    }

    [HttpPost("{id:guid}/tasks")]
    public async Task<ActionResult<TaskItemApiResponse>> CreateTask(Guid id, CreateTaskItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Title is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BoardType))
        {
            return BadRequest("Board Type is required.");
        }

        if (request.ColumnId == Guid.Empty)
        {
            return BadRequest("Column is required.");
        }

        var assigneeIds = (request.AssignedUserIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (request.AssignedUserId.HasValue && request.AssignedUserId != Guid.Empty
            && !assigneeIds.Contains(request.AssignedUserId.Value))
        {
            assigneeIds.Add(request.AssignedUserId.Value);
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            BoardType = string.IsNullOrWhiteSpace(request.BoardType) ? null : request.BoardType.Trim(),
            ProjectId = id,
            ColumnId = request.ColumnId,
            AssignedUserId = assigneeIds.FirstOrDefault() == Guid.Empty ? null : assigneeIds.FirstOrDefault(),
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority,
            Category = request.TagId.HasValue ? request.TagId.Value.ToString() : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Project = new Project { Id = id },
            Column = new ProjectColumn { Id = request.ColumnId }
        };

        await using var connection = await _context.CreateOpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string insertTaskSql = @"
            INSERT INTO task_item
                (id, title, description, board_type, project_id, column_id, assigned_user_id, start_date, due_date, priority, category, created_at, updated_at)
            VALUES
                (@Id, @Title, @Description, @BoardType, @ProjectId, @ColumnId, @AssignedUserId, @StartDate, @DueDate, @Priority, @Category, @CreatedAt, @UpdatedAt)";

        await connection.ExecuteAsync(insertTaskSql, task, transaction);

        if (request.TagId.HasValue)
        {
            const string insertTaskTagSql = @"
                INSERT INTO task_item_tag (task_id, tag_id)
                VALUES (@TaskId, @TagId)";

            await connection.ExecuteAsync(insertTaskTagSql, new { TaskId = task.Id, TagId = request.TagId.Value }, transaction);
        }

        if (assigneeIds.Count > 0)
        {
            const string insertAssigneeSql = @"
                INSERT INTO task_assignee (task_id, user_id, assigned_at)
                VALUES (@TaskId, @UserId, @AssignedAt)";

            foreach (var userId in assigneeIds)
            {
                await connection.ExecuteAsync(insertAssigneeSql, new
                {
                    TaskId = task.Id,
                    UserId = userId,
                    AssignedAt = DateTime.UtcNow,
                }, transaction);
            }
        }

        await transaction.CommitAsync();

        return Ok(new TaskItemApiResponse
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            ColumnId = task.ColumnId,
            AssignedUserName = null,
            DueDate = task.DueDate,
            Priority = task.Priority,
            Tags = request.TagId.HasValue ? new List<ProjectTagApiResponse> { new() { Id = request.TagId.Value, Name = string.Empty } } : new List<ProjectTagApiResponse>()
        });
    }

    [HttpGet("{projectId:guid}/tasks/{taskId:guid}")]
    public async Task<ActionResult<TaskDetailApiResponse>> GetTask(Guid projectId, Guid taskId)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string taskSql = @"
            SELECT ti.id, ti.title, ti.description, ti.project_id AS ProjectId, ti.column_id AS ColumnId,
                   ti.board_type AS BoardType, ti.assigned_user_id AS AssignedUserId,
                   ti.start_date AS StartDate, ti.due_date AS DueDate, ti.priority, ti.category AS Category,
                   ti.created_at AS CreatedAt, ti.updated_at AS UpdatedAt,
                   u.full_name AS AssignedUserName, u.email AS AssignedUserEmail,
                   pc.name AS ColumnName
            FROM task_item ti
            LEFT JOIN users u ON u.id = ti.assigned_user_id
            LEFT JOIN project_column pc ON pc.id = ti.column_id
            WHERE ti.id = @TaskId AND ti.project_id = @ProjectId";

        var taskRows = await connection.QueryAsync<TaskDetailRow>(taskSql, new { TaskId = taskId, ProjectId = projectId });
        var taskRow = taskRows.FirstOrDefault();
        if (taskRow == null)
        {
            return NotFound();
        }

        const string projectSql = "SELECT name FROM project WHERE id = @ProjectId";
        var projectName = await connection.QuerySingleOrDefaultAsync<string>(projectSql, new { ProjectId = projectId });

        const string checklistSql = @"
            SELECT id, task_id AS TaskId, label AS Label, is_completed AS IsCompleted,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM task_checklist_item
            WHERE task_id = @TaskId
            ORDER BY created_at";

        var checklist = (await connection.QueryAsync<TaskChecklistItem>(checklistSql, new { TaskId = taskId })).ToList();

        const string attachmentsSql = @"
            SELECT id, task_id AS TaskId, file_name AS FileName, file_path AS FilePath,
                   file_type AS FileType, file_size AS FileSize, uploaded_at AS UploadedAt
            FROM task_attachment
            WHERE task_id = @TaskId
            ORDER BY uploaded_at DESC";

        var attachments = (await connection.QueryAsync<TaskAttachment>(attachmentsSql, new { TaskId = taskId })).ToList();

        const string tagsSql = @"
            SELECT pt.id, pt.name, pt.color
            FROM task_item_tag tit
            JOIN project_tag pt ON pt.id = tit.tag_id
            WHERE tit.task_id = @TaskId";

        var tags = (await connection.QueryAsync<ProjectTag>(tagsSql, new { TaskId = taskId })).ToList();

        const string assigneesSql = @"
            SELECT ta.user_id AS UserId, u.full_name AS FullName, u.email AS Email
            FROM task_assignee ta
            JOIN users u ON u.id = ta.user_id
            WHERE ta.task_id = @TaskId
            ORDER BY ta.assigned_at";

        var assignees = (await connection.QueryAsync<TaskAssigneeApiResponse>(assigneesSql, new { TaskId = taskId })).ToList();

        if (assignees.Count == 0 && taskRow.AssignedUserId.HasValue
            && !string.IsNullOrWhiteSpace(taskRow.AssignedUserName))
        {
            assignees.Add(new TaskAssigneeApiResponse
            {
                UserId = taskRow.AssignedUserId.Value,
                FullName = taskRow.AssignedUserName,
                Email = taskRow.AssignedUserEmail ?? string.Empty,
            });
        }

        var categoryName = ResolveCategoryName(taskRow.Category, tags);
        if (string.IsNullOrWhiteSpace(categoryName) && Guid.TryParse(taskRow.Category, out var categoryId))
        {
            categoryName = await connection.QuerySingleOrDefaultAsync<string>(
                "SELECT name FROM project_tag WHERE id = @Id",
                new { Id = categoryId });
        }

        if (string.IsNullOrWhiteSpace(categoryName) && tags.Count > 0)
        {
            categoryName = tags[0].Name;
        }

        var completedCount = checklist.Count(item => item.IsCompleted);
        var progress = checklist.Count == 0 ? 0 : (int)Math.Round(completedCount * 100.0 / checklist.Count);

        return Ok(new TaskDetailApiResponse
        {
            Id = taskRow.Id,
            Title = taskRow.Title,
            Description = taskRow.Description,
            ProjectId = projectId,
            ProjectName = projectName ?? string.Empty,
            ColumnId = taskRow.ColumnId,
            ColumnName = taskRow.ColumnName ?? string.Empty,
            BoardType = taskRow.BoardType,
            AssignedUserId = taskRow.AssignedUserId,
            AssignedUserName = taskRow.AssignedUserName,
            AssignedUserEmail = taskRow.AssignedUserEmail,
            Assignees = assignees,
            StartDate = taskRow.StartDate,
            DueDate = taskRow.DueDate,
            Priority = taskRow.Priority ?? string.Empty,
            Category = categoryName,
            CreatedAt = taskRow.CreatedAt,
            UpdatedAt = taskRow.UpdatedAt,
            Progress = progress,
            Tags = tags.Select(tag => new ProjectTagApiResponse
            {
                Id = tag.Id,
                Name = tag.Name,
                Color = tag.Color,
            }).ToList(),
            Checklist = checklist.Select(item => new TaskChecklistItemApiResponse
            {
                Id = item.Id,
                Label = item.Label,
                IsCompleted = item.IsCompleted,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
            }).ToList(),
            Attachments = attachments.Select(attachment => new TaskAttachmentApiResponse
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                FileType = attachment.FileType,
                FileSize = attachment.FileSize,
                UploadedAt = attachment.UploadedAt,
            }).ToList(),
        });
    }

    [HttpPost("{projectId:guid}/tasks/{taskId:guid}/checklist")]
    public async Task<ActionResult<TaskChecklistItemApiResponse>> AddChecklistItem(
        Guid projectId,
        Guid taskId,
        CreateChecklistItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return BadRequest("Label is required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string taskExistsSql = "SELECT COUNT(1) FROM task_item WHERE id = @TaskId AND project_id = @ProjectId";
        var taskExists = await connection.ExecuteScalarAsync<int>(taskExistsSql, new { TaskId = taskId, ProjectId = projectId }) > 0;
        if (!taskExists)
        {
            return NotFound();
        }

        var item = new TaskChecklistItem
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Label = request.Label.Trim(),
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        const string insertSql = @"
            INSERT INTO task_checklist_item (id, task_id, label, is_completed, created_at, updated_at)
            VALUES (@Id, @TaskId, @Label, @IsCompleted, @CreatedAt, @UpdatedAt)";

        await connection.ExecuteAsync(insertSql, item);

        return Ok(new TaskChecklistItemApiResponse
        {
            Id = item.Id,
            Label = item.Label,
            IsCompleted = item.IsCompleted,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
        });
    }

    [HttpPost("{projectId:guid}/columns")]
    public async Task<ActionResult<ProjectColumnApiResponse>> CreateColumn(Guid projectId, CreateProjectColumnRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return BadRequest("A column name of up to 100 characters is required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string projectExistsSql = "SELECT COUNT(1) FROM project WHERE id = @ProjectId";
        var projectExists = await connection.ExecuteScalarAsync<int>(projectExistsSql, new { ProjectId = projectId }) > 0;
        if (!projectExists)
        {
            return NotFound();
        }

        const string nextPositionSql = "SELECT COALESCE(MAX(position), -1) + 1 FROM project_column WHERE project_id = @ProjectId";
        var position = await connection.ExecuteScalarAsync<int>(nextPositionSql, new { ProjectId = projectId });
        var column = new ProjectColumn
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            Position = position,
            CreatedAt = DateTime.UtcNow,
        };

        const string insertColumnSql = "INSERT INTO project_column (id, project_id, name, position, created_at) VALUES (@Id, @ProjectId, @Name, @Position, @CreatedAt)";
        await connection.ExecuteAsync(insertColumnSql, column);

        return Ok(new ProjectColumnApiResponse
        {
            Id = column.Id,
            Name = column.Name,
            Position = column.Position,
        });
    }

    [HttpPut("{projectId:guid}/columns/{columnId:guid}")]
    public async Task<ActionResult<ProjectColumnApiResponse>> UpdateColumn(
        Guid projectId,
        Guid columnId,
        UpdateProjectColumnRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return BadRequest("A column name of up to 100 characters is required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string updateSql = @"
            UPDATE project_column
            SET name = @Name
            WHERE id = @ColumnId AND project_id = @ProjectId
            RETURNING id AS Id, name AS Name, position AS Position";

        var column = await connection.QuerySingleOrDefaultAsync<ProjectColumnApiResponse>(updateSql, new
        {
            Name = name,
            ColumnId = columnId,
            ProjectId = projectId,
        });

        return column == null ? NotFound() : Ok(column);
    }

    [HttpPut("{projectId:guid}/columns/order")]
    public async Task<IActionResult> ReorderColumns(Guid projectId, ReorderProjectColumnsRequest request)
    {
        if (request.ColumnIds.Count == 0 || request.ColumnIds.Distinct().Count() != request.ColumnIds.Count)
        {
            return BadRequest("A unique ordered list of columns is required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        var currentColumnIds = (await connection.QueryAsync<Guid>(
            "SELECT id FROM project_column WHERE project_id = @ProjectId",
            new { ProjectId = projectId })).ToList();
        if (currentColumnIds.Count == 0 && request.ColumnIds.Count == 0)
        {
            return NoContent();
        }
        if (currentColumnIds.Count != request.ColumnIds.Count || !currentColumnIds.All(request.ColumnIds.Contains))
        {
            return BadRequest("The column order must include every column in the project exactly once.");
        }

        await using var transaction = await connection.BeginTransactionAsync();
        const string updatePositionSql = "UPDATE project_column SET position = @Position WHERE id = @ColumnId AND project_id = @ProjectId";
        for (var position = 0; position < request.ColumnIds.Count; position++)
        {
            await connection.ExecuteAsync(updatePositionSql, new
            {
                ColumnId = request.ColumnIds[position],
                ProjectId = projectId,
                Position = position,
            }, transaction);
        }
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpPost("{projectId:guid}/tasks/{taskId:guid}/assignees/{userId:guid}")]
    public async Task<ActionResult<TaskAssigneeApiResponse>> AddTaskAssignee(
        Guid projectId,
        Guid taskId,
        Guid userId)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string taskExistsSql = "SELECT COUNT(1) FROM task_item WHERE id = @TaskId AND project_id = @ProjectId";
        var taskExists = await connection.ExecuteScalarAsync<int>(taskExistsSql, new { TaskId = taskId, ProjectId = projectId }) > 0;
        if (!taskExists)
        {
            return NotFound();
        }

        const string collaboratorSql = @"
            SELECT u.id AS UserId, u.full_name AS FullName, u.email AS Email
            FROM project_collaborator pc
            JOIN users u ON u.id = pc.user_id
            WHERE pc.project_id = @ProjectId AND pc.user_id = @UserId";

        var collaborator = await connection.QuerySingleOrDefaultAsync<TaskAssigneeApiResponse>(collaboratorSql, new
        {
            ProjectId = projectId,
            UserId = userId,
        });

        if (collaborator == null)
        {
            return BadRequest("Only project collaborators can be assigned to this task.");
        }

        const string insertSql = @"
            INSERT INTO task_assignee (task_id, user_id, assigned_at)
            VALUES (@TaskId, @UserId, @AssignedAt)
            ON CONFLICT (task_id, user_id) DO NOTHING";

        await connection.ExecuteAsync(insertSql, new
        {
            TaskId = taskId,
            UserId = userId,
            AssignedAt = DateTime.UtcNow,
        });

        const string setPrimaryAssigneeSql = @"
            UPDATE task_item
            SET assigned_user_id = COALESCE(assigned_user_id, @UserId), updated_at = @UpdatedAt
            WHERE id = @TaskId AND project_id = @ProjectId";

        await connection.ExecuteAsync(setPrimaryAssigneeSql, new
        {
            TaskId = taskId,
            ProjectId = projectId,
            UserId = userId,
            UpdatedAt = DateTime.UtcNow,
        });

        return Ok(collaborator);
    }

    [HttpDelete("{projectId:guid}/tasks/{taskId:guid}/assignees/{userId:guid}")]
    public async Task<IActionResult> RemoveTaskAssignee(Guid projectId, Guid taskId, Guid userId)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string taskExistsSql = "SELECT COUNT(1) FROM task_item WHERE id = @TaskId AND project_id = @ProjectId";
        var taskExists = await connection.ExecuteScalarAsync<int>(taskExistsSql, new { TaskId = taskId, ProjectId = projectId }, transaction) > 0;
        if (!taskExists)
        {
            return NotFound();
        }

        const string ownerSql = "SELECT user_id FROM project WHERE id = @ProjectId";
        var ownerUserId = await connection.ExecuteScalarAsync<Guid?>(ownerSql, new { ProjectId = projectId }, transaction);
        if (ownerUserId == userId)
        {
            return BadRequest("The project owner cannot be removed from a task.");
        }

        const string deleteSql = "DELETE FROM task_assignee WHERE task_id = @TaskId AND user_id = @UserId";
        var deleted = await connection.ExecuteAsync(deleteSql, new { TaskId = taskId, UserId = userId }, transaction);
        if (deleted == 0)
        {
            return NotFound();
        }

        const string updatePrimaryAssigneeSql = @"
            UPDATE task_item
            SET assigned_user_id = (
                    SELECT user_id FROM task_assignee WHERE task_id = @TaskId ORDER BY assigned_at LIMIT 1
                ),
                updated_at = @UpdatedAt
            WHERE id = @TaskId AND project_id = @ProjectId";

        await connection.ExecuteAsync(updatePrimaryAssigneeSql, new
        {
            TaskId = taskId,
            ProjectId = projectId,
            UpdatedAt = DateTime.UtcNow,
        }, transaction);

        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpPut("{projectId:guid}/tasks/{taskId:guid}/checklist/{itemId:guid}")]
    public async Task<ActionResult<TaskChecklistItemApiResponse>> UpdateChecklistItem(
        Guid projectId,
        Guid taskId,
        Guid itemId,
        UpdateChecklistItemRequest request)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string existingSql = @"
            SELECT ci.id, ci.task_id AS TaskId, ci.label AS Label, ci.is_completed AS IsCompleted,
                   ci.created_at AS CreatedAt, ci.updated_at AS UpdatedAt
            FROM task_checklist_item ci
            JOIN task_item ti ON ti.id = ci.task_id
            WHERE ci.id = @ItemId AND ci.task_id = @TaskId AND ti.project_id = @ProjectId";

        var existing = await connection.QuerySingleOrDefaultAsync<TaskChecklistItem>(existingSql, new
        {
            ItemId = itemId,
            TaskId = taskId,
            ProjectId = projectId,
        });

        if (existing == null)
        {
            return NotFound();
        }

        if (request.IsCompleted.HasValue)
        {
            existing.IsCompleted = request.IsCompleted.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Label))
        {
            existing.Label = request.Label.Trim();
        }

        existing.UpdatedAt = DateTime.UtcNow;

        const string updateSql = @"
            UPDATE task_checklist_item
            SET label = @Label, is_completed = @IsCompleted, updated_at = @UpdatedAt
            WHERE id = @Id AND task_id = @TaskId";

        await connection.ExecuteAsync(updateSql, existing);

        return Ok(new TaskChecklistItemApiResponse
        {
            Id = existing.Id,
            Label = existing.Label,
            IsCompleted = existing.IsCompleted,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt,
        });
    }

    [HttpPut("{projectId:guid}/tasks/{taskId:guid}")]
    public async Task<IActionResult> MoveTask(Guid projectId, Guid taskId, [FromBody] MoveTaskRequest request)
    {
        if (request == null || request.ColumnId == Guid.Empty)
        {
            return BadRequest("Column is required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string updateSql = @"
            UPDATE task_item
            SET column_id = @ColumnId,
                updated_at = @UpdatedAt
            WHERE id = @TaskId AND project_id = @ProjectId";

        var rows = await connection.ExecuteAsync(updateSql, new
        {
            ColumnId = request.ColumnId,
            UpdatedAt = DateTime.UtcNow,
            TaskId = taskId,
            ProjectId = projectId,
        });

        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{projectId:guid}/tasks/{taskId:guid}")]
    public async Task<IActionResult> DeleteTask(Guid projectId, Guid taskId)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        const string deleteSql = "DELETE FROM task_item WHERE id = @TaskId AND project_id = @ProjectId";
        var rows = await connection.ExecuteAsync(deleteSql, new { TaskId = taskId, ProjectId = projectId });

        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
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
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Unauthorized();
        }

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string ownerSql = "SELECT user_id FROM project WHERE id = @Id";
        var ownerUserId = await connection.QuerySingleOrDefaultAsync<Guid?>(ownerSql, new { Id = id });
        if (!ownerUserId.HasValue)
        {
            return NotFound();
        }
        if (ownerUserId.Value != currentUserId)
        {
            return Forbid();
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

    [HttpDelete("{id:guid}/collaborators/{userId:guid}")]
    public async Task<IActionResult> RemoveCollaborator(Guid id, Guid userId)
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Unauthorized();
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string ownerSql = "SELECT user_id FROM project WHERE id = @Id";
        var ownerUserId = await connection.QuerySingleOrDefaultAsync<Guid?>(ownerSql, new { Id = id });
        if (!ownerUserId.HasValue)
        {
            return NotFound();
        }
        if (ownerUserId.Value != currentUserId)
        {
            return Forbid();
        }
        if (ownerUserId.Value == userId)
        {
            return BadRequest("The project owner cannot be removed as a collaborator.");
        }

        await using var transaction = await connection.BeginTransactionAsync();
        const string deleteCollaboratorSql = "DELETE FROM project_collaborator WHERE project_id = @ProjectId AND user_id = @UserId";
        var deleted = await connection.ExecuteAsync(deleteCollaboratorSql, new { ProjectId = id, UserId = userId }, transaction);
        if (deleted == 0)
        {
            return NotFound();
        }

        const string removeTaskAssigneeSql = @"
            DELETE FROM task_assignee ta
            USING task_item ti
            WHERE ta.task_id = ti.id
              AND ti.project_id = @ProjectId
              AND ta.user_id = @UserId";
        await connection.ExecuteAsync(removeTaskAssigneeSql, new { ProjectId = id, UserId = userId }, transaction);

        const string updatePrimaryAssigneeSql = @"
            UPDATE task_item ti
            SET assigned_user_id = (
                    SELECT ta.user_id
                    FROM task_assignee ta
                    WHERE ta.task_id = ti.id
                    ORDER BY ta.assigned_at
                    LIMIT 1
                ),
                updated_at = @UpdatedAt
            WHERE ti.project_id = @ProjectId
              AND ti.assigned_user_id = @UserId";
        await connection.ExecuteAsync(updatePrimaryAssigneeSql, new
        {
            ProjectId = id,
            UserId = userId,
            UpdatedAt = DateTime.UtcNow,
        }, transaction);

        await transaction.CommitAsync();
        return NoContent();
    }

    private static string? ResolveCategoryName(string? category, IReadOnlyList<ProjectTag> tags)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        if (Guid.TryParse(category, out var categoryId))
        {
            return tags.FirstOrDefault(tag => tag.Id == categoryId)?.Name;
        }

        return category;
    }

    private static ProjectApiResponse MapProject(Project project)
    {
        return new ProjectApiResponse
        {
            Id = project.Id,
            UserId = project.UserId,
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
                    Assignees = task.Assignees
                        .Select(assignee => new TaskAssigneeApiResponse
                        {
                            UserId = assignee.UserId,
                            FullName = assignee.User?.FullName ?? string.Empty,
                            Email = assignee.User?.Email ?? string.Empty,
                        })
                        .ToList(),
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
    public Guid UserId { get; set; }
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

public sealed class CreateProjectColumnRequest
{
    public string? Name { get; set; }
}

public sealed class UpdateProjectColumnRequest
{
    public string? Name { get; set; }
}

public sealed class ReorderProjectColumnsRequest
{
    public List<Guid> ColumnIds { get; set; } = [];
}

public class MoveTaskRequest
{
    public Guid ColumnId { get; set; }
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

public sealed class CreateTaskItemRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BoardType { get; set; } = string.Empty;
    public Guid ColumnId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public List<Guid>? AssignedUserIds { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public Guid? TagId { get; set; }
}
public class TaskItemApiResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid ColumnId { get; set; }
    public string? AssignedUserName { get; set; }
    public List<TaskAssigneeApiResponse> Assignees { get; set; } = [];
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

public sealed class TaskDetailRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid ProjectId { get; init; }
    public Guid ColumnId { get; init; }
    public string? BoardType { get; init; }
    public Guid? AssignedUserId { get; init; }
    public string? AssignedUserName { get; init; }
    public string? AssignedUserEmail { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? DueDate { get; init; }
    public string? Priority { get; init; }
    public string? Category { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? ColumnName { get; init; }
}

public sealed class TaskDetailApiResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public Guid ColumnId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string? BoardType { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string? AssignedUserEmail { get; set; }
    public List<TaskAssigneeApiResponse> Assignees { get; set; } = [];
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? Category { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Progress { get; set; }
    public List<ProjectTagApiResponse> Tags { get; set; } = [];
    public List<TaskChecklistItemApiResponse> Checklist { get; set; } = [];
    public List<TaskAttachmentApiResponse> Attachments { get; set; } = [];
}

public sealed class TaskChecklistItemApiResponse
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TaskAttachmentApiResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FileType { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
}

public sealed class TaskAssigneeApiResponse
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class CreateChecklistItemRequest
{
    public string Label { get; set; } = string.Empty;
}

public sealed class UpdateChecklistItemRequest
{
    public string? Label { get; set; }
    public bool? IsCompleted { get; set; }
}
