using CMS.Data;
using CMS.Models;
using CMS.Enum;
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


    // GET: api/projects
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectApiResponse>>> GetAll()
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT PROJECT_ID AS Id
                , USER_ID AS UserId
                , NAME
                , DESCRIPTION
                , CREATED_DATE AS CreatedAt
                , UPDATED_DATE AS UpdatedAt
            FROM MM_PROJECT
            WHERE STATUS = 1;
        ";

        var projects = (await connection.QueryAsync<Project>(sql)).ToList();
        return Ok(projects.Select(MapProject).ToList());
    }

    // GET: api/projects/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectApiResponse>> Get(Guid id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string projectSql = @"
            SELECT PROJECT_ID AS Id
                , USER_ID AS UserId
                , NAME
                , DESCRIPTION
                , CREATED_DATE AS CreatedAt
                , UPDATED_DATE AS UpdatedAt
            FROM MM_PROJECT
            WHERE PROJECT_ID = @Id
                AND STATUS = 1;
        ";

        var project = await connection.QuerySingleOrDefaultAsync<Project>(projectSql, new { Id = id });
        if (project == null)
        {
            return NotFound();
        }

        const string columnsSql = @"
            SELECT PROJECT_COLUMN_ID AS Id
                , PROJECT_ID AS ProjectId
                , NAME
                , POSITION
                , CREATED_DATE AS CreatedAt
            FROM DE_PROJECT_COLUMN
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1
            ORDER BY POSITION;
        ";

        project.Columns = (await connection.QueryAsync<ProjectColumn>(columnsSql, new { ProjectId = id })).ToList();

        const string tagsSql = @"
            SELECT PROJECT_TAG_ID AS Id
                , PROJECT_ID AS ProjectId
                , NAME
                , COLOR
                , CREATED_DATE AS CreatedAt
            FROM MM_PROJECT_TAG
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        project.Tags = (await connection.QueryAsync<ProjectTag>(tagsSql, new { ProjectId = id })).ToList();

        const string collaboratorsSql = @"
            SELECT PC.PROJECT_ID AS ProjectId
                , PC.USER_ID AS UserId
                , PC.ROLE AS ROLE
                , PC.CREATED_DATE AS JoinedAt
                , U.USER_ID AS Id
                , U.FULL_NAME AS FullName
                , U.EMAIL AS Email
                , U.CREATED_DATE AS CreatedAt
            FROM DE_PROJECT_COLLABORATOR PC
            LEFT JOIN MM_USER U ON U.USER_ID = PC.USER_ID
            WHERE PC.PROJECT_ID = @ProjectId
                AND PC.STATUS = 1;
        ";

        project.Collaborators = (await connection.QueryAsync<ProjectCollaborator, User, ProjectCollaborator>(
            collaboratorsSql,
            (collaborator, user) =>
            {
                collaborator.User = user;
                return collaborator;
            },
            new { ProjectId = id },
            splitOn: "Id")).ToList();

        const string tasksSql = @"
            SELECT TI.TASK_ITEM_ID AS Id
                , TI.TITLE
                , TI.DESCRIPTION
                , TI.PROJECT_ID AS ProjectId
                , TI.PROJECT_COLUMN_ID AS ColumnId
                , TI.ASSIGNED_USER_ID AS AssignedUserId
                , TI.START_DATE AS StartDate
                , TI.DUE_DATE AS DueDate
                , TI.PRIORITY
                , TI.CREATED_DATE AS CreatedAt
                , TI.UPDATED_DATE AS UpdatedAt
                , U.USER_ID AS Id
                , U.FULL_NAME AS FullName
                , U.EMAIL AS Email
                , U.CREATED_DATE AS CreatedAt
            FROM DE_TASK_ITEM TI
            LEFT JOIN MM_USER U ON U.USER_ID = TI.ASSIGNED_USER_ID
            WHERE TI.PROJECT_ID = @ProjectId
                AND TI.STATUS = 1
        ";

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
                SELECT TA.TASK_ITEM_ID AS TaskId
                    , TA.USER_ID AS UserId
                    , TA.CREATED_DATE AS AssignedAt
                    , U.USER_ID AS Id
                    , U.FULL_NAME AS FullName
                    , U.EMAIL AS Email
                    , U.CREATED_DATE AS CreatedAt
                FROM DE_TASK_ASSIGNEE TA
                JOIN MM_USER U ON U.USER_ID = TA.USER_ID
                WHERE TA.TASK_ITEM_ID = ANY (@TaskIds)
                    AND TA.STATUS = 1
                ORDER BY TA.CREATED_DATE;";

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
                SELECT TIT.TASK_ITEM_ID AS TaskId
                    , TIT.PROJECT_TAG_ID AS TagId
                    , PT.PROJECT_TAG_ID AS Id
                    , PT.NAME AS Name
                    , PT.COLOR AS Color
                FROM DE_TASK_ITEM_TAG TIT
                JOIN MM_PROJECT_TAG PT ON PT.PROJECT_TAG_ID = TIT.PROJECT_TAG_ID
                WHERE TIT.TASK_ITEM_ID = ANY (@TaskIds)
                    AND TIT.STATUS = 1;
            ";

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
                SELECT TASK_ATTACHMENT_ID AS Id
                    , TASK_ITEM_ID AS TaskId
                    , FILE_NAME AS FileName
                    , FILE_PATH AS FilePath
                    , FILE_TYPE AS FileType
                    , FILE_SIZE AS FileSize
                    , CREATED_DATE AS UploadedAt
                FROM DE_TASK_ATTACHMENT
                WHERE TASK_ITEM_ID = ANY (@TaskIds)
                    AND STATUS = 1;
            ";

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

    // POST: api/projects
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

        const string insertProjectSql = @"
            INSERT INTO MM_PROJECT (
                PROJECT_ID
                , USER_ID
                , NAME
                , DESCRIPTION
                , CREATED_DATE
                , UPDATED_DATE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_LOC
                )
            VALUES (
                @Id
                , @UserId
                , @Name
                , @Description
                , @CreatedAt
                , @UpdatedAt
                , 1
                , 'SYSTEM'
                , '127.0.0.1'
                , 'SYSTEM'
                , '127.0.0.1'
            );
        ";

        await connection.ExecuteAsync(insertProjectSql, project, transaction);

        const string insertColumnSql = @"
            INSERT INTO DE_PROJECT_COLUMN (
                PROJECT_COLUMN_ID
                , PROJECT_ID
                , NAME
                , POSITION
                , CREATED_DATE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_DATE
                , UPDATED_LOC
                )
            VALUES (
                @Id
                , @ProjectId
                , @Name
                , @Position
                , @CreatedAt
                , 1
                , 'SYSTEM'
                , '127.0.0.1'
                , 'SYSTEM'
                , @CreatedAt
                , '127.0.0.1'
            );
        ";

        foreach (var column in project.Columns)
        {
            column.Id = column.Id == Guid.Empty ? Guid.NewGuid() : column.Id;
            column.ProjectId = project.Id;
            column.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(insertColumnSql, column, transaction);
        }

        const string insertTagSql = @"
            INSERT INTO MM_PROJECT_TAG (
                PROJECT_TAG_ID
                , PROJECT_ID
                , NAME
                , COLOR
                , CREATED_DATE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_DATE
                , UPDATED_LOC
                )
            VALUES (
                @Id
                , @ProjectId
                , @Name
                , @Color
                , @CreatedAt
                , 1
                , 'SYSTEM'
                , '127.0.0.1'
                , 'SYSTEM'
                , @CreatedAt
                , '127.0.0.1'
            );
        ";

        foreach (var tag in project.Tags)
        {
            tag.Id = tag.Id == Guid.Empty ? Guid.NewGuid() : tag.Id;
            tag.ProjectId = project.Id;
            tag.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(insertTagSql, tag, transaction);
        }

        const string insertCollaboratorSql = @"
            INSERT INTO DE_PROJECT_COLLABORATOR (
                PROJECT_ID
                , USER_ID
                , ROLE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_DATE
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_DATE
                , UPDATED_LOC
                )
            VALUES (
                @ProjectId
                , @UserId
                , @Role
                , 1
                , 'SYSTEM'
                , @JoinedAt
                , '127.0.0.1'
                , 'SYSTEM'
                , @JoinedAt
                , '127.0.0.1'
            );
        ";

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

    // POST: api/projects/{id}/tasks
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
            Priority = request.Priority,
            Category = request.TagId.HasValue ? request.TagId.Value.ToString() : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Project = new Project { Id = id },
            Column = new ProjectColumn { Id = request.ColumnId }
        };

        await using var connection = await _context.CreateOpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string insertTaskSql = @"
            INSERT INTO DE_TASK_ITEM (
                TASK_ITEM_ID
                , TITLE
                , DESCRIPTION
                , BOARD_TYPE
                , PROJECT_ID
                , PROJECT_COLUMN_ID
                , ASSIGNED_USER_ID
                , START_DATE
                , DUE_DATE
                , PRIORITY
                , CATEGORY
                , CREATED_DATE
                , UPDATED_DATE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_LOC
                )
            VALUES (
                @Id
                , @Title
                , @Description
                , @BoardType
                , @ProjectId
                , @ColumnId
                , @AssignedUserId
                , @StartDate
                , @DueDate
                , @Priority
                , @Category
                , @CreatedAt
                , @UpdatedAt
                , 1
                , 'SYSTEM'
                , '127.0.0.1'
                , 'SYSTEM'
                , '127.0.0.1'
            );
        ";

        await connection.ExecuteAsync(insertTaskSql, task, transaction);

        if (request.TagId.HasValue)
        {
            const string insertTaskTagSql = @"
                INSERT INTO DE_TASK_ITEM_TAG (
                    TASK_ITEM_ID
                    , PROJECT_TAG_ID
                    , RECORD_TYP
                    , CREATED_BY
                    , CREATED_DATE
                    , CREATED_LOC
                    , UPDATED_BY
                    , UPDATED_DATE
                    , UPDATED_LOC
                    )
                VALUES (
                    @TaskId
                    , @TagId
                    , 1
                    , 'SYSTEM'
                    , CURRENT_TIMESTAMP
                    , '127.0.0.1'
                    , 'SYSTEM'
                    , CURRENT_TIMESTAMP
                    , '127.0.0.1'
                );
            ";

            await connection.ExecuteAsync(insertTaskTagSql, new { TaskId = task.Id, TagId = request.TagId.Value }, transaction);
        }

        if (assigneeIds.Count > 0)
        {
            const string insertAssigneeSql = @"
                INSERT INTO DE_TASK_ASSIGNEE (
                    TASK_ITEM_ID
                    , USER_ID
                    , ASSIGNED_AT
                    , RECORD_TYP
                    , CREATED_BY
                    , CREATED_DATE
                    , CREATED_LOC
                    , UPDATED_BY
                    , UPDATED_DATE
                    , UPDATED_LOC
                    )
                VALUES (
                    @TaskId
                    , @UserId
                    , @AssignedAt
                    , 1
                    , 'SYSTEM'
                    , @AssignedAt
                    , '127.0.0.1'
                    , 'SYSTEM'
                    , @AssignedAt
                    , '127.0.0.1'
                );
            ";

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
            SELECT TI.TASK_ITEM_ID AS Id
                , TI.TITLE
                , TI.DESCRIPTION
                , TI.PROJECT_ID AS ProjectId
                , TI.PROJECT_COLUMN_ID AS ColumnId
                , TI.BOARD_TYPE AS BoardType
                , TI.ASSIGNED_USER_ID AS AssignedUserId
                , TI.START_DATE AS StartDate
                , TI.DUE_DATE AS DueDate
                , TI.PRIORITY
                , TI.CATEGORY AS Category
                , TI.CREATED_DATE AS CreatedAt
                , TI.UPDATED_DATE AS UpdatedAt
                , U.FULL_NAME AS AssignedUserName
                , U.EMAIL AS AssignedUserEmail
                , PC.NAME AS ColumnName
            FROM DE_TASK_ITEM TI
            LEFT JOIN MM_USER U ON U.USER_ID = TI.ASSIGNED_USER_ID
            LEFT JOIN DE_PROJECT_COLUMN PC ON PC.PROJECT_COLUMN_ID = TI.PROJECT_COLUMN_ID
            WHERE TI.TASK_ITEM_ID = @TaskId
                AND TI.PROJECT_ID = @ProjectId
                AND TI.STATUS = 1;
        ";

        var taskRows = await connection.QueryAsync<TaskDetailRow>(taskSql, new { TaskId = taskId, ProjectId = projectId });
        var taskRow = taskRows.FirstOrDefault();
        if (taskRow == null)
        {
            return NotFound();
        }

        const string projectSql = @"
            SELECT NAME
            FROM MM_PROJECT
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var projectName = await connection.QuerySingleOrDefaultAsync<string>(projectSql, new { ProjectId = projectId });

        const string checklistSql = @"
            SELECT TASK_CHECKLIST_ITEM_ID AS Id
                , TASK_ITEM_ID AS TaskId
                , LABEL AS Label
                , IS_COMPLETED AS IsCompleted
                , CREATED_DATE AS CreatedAt
                , UPDATED_DATE AS UpdatedAt
            FROM DE_TASK_CHECKLIST_ITEM
            WHERE TASK_ITEM_ID = @TaskId
                AND STATUS = 1
            ORDER BY CREATED_DATE;
        ";

        var checklist = (await connection.QueryAsync<TaskChecklistItem>(checklistSql, new { TaskId = taskId })).ToList();

        const string attachmentsSql = @"
            SELECT TASK_ATTACHMENT_ID AS Id
                , TASK_ITEM_ID AS TaskId
                , FILE_NAME AS FileName
                , FILE_PATH AS FilePath
                , FILE_TYPE AS FileType
                , FILE_SIZE AS FileSize
                , CREATED_DATE AS UploadedAt
            FROM DE_TASK_ATTACHMENT
            WHERE TASK_ITEM_ID = @TaskId
                AND STATUS = 1
            ORDER BY CREATED_DATE DESC;
        ";

        var attachments = (await connection.QueryAsync<TaskAttachment>(attachmentsSql, new { TaskId = taskId })).ToList();

        const string tagsSql = @"
            SELECT PT.PROJECT_TAG_ID AS Id
                , PT.NAME AS Name
                , PT.COLOR AS Color
            FROM DE_TASK_ITEM_TAG TIT
            JOIN MM_PROJECT_TAG PT ON PT.PROJECT_TAG_ID = TIT.PROJECT_TAG_ID
            WHERE TIT.TASK_ITEM_ID = @TaskId
                AND TIT.STATUS = 1;
        ";

        var tags = (await connection.QueryAsync<ProjectTag>(tagsSql, new { TaskId = taskId })).ToList();

        const string assigneesSql = @"
            SELECT TA.USER_ID AS UserId
                , U.FULL_NAME AS FullName
                , U.EMAIL AS Email
            FROM DE_TASK_ASSIGNEE TA
            JOIN MM_USER U ON U.USER_ID = TA.USER_ID
            WHERE TA.TASK_ITEM_ID = @TaskId
                AND TA.STATUS = 1
            ORDER BY TA.CREATED_DATE;
        ";

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
            const string categorySql = @"
                SELECT NAME
                FROM MM_PROJECT_TAG
                WHERE PROJECT_TAG_ID = @Id
                    AND STATUS = 1;
            ";

            categoryName = await connection.QuerySingleOrDefaultAsync<string>(categorySql, new { Id = categoryId });
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
            Priority = taskRow.Priority ?? default,
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

        const string taskExistsSql = @"
            SELECT COUNT(1)
            FROM DE_TASK_ITEM
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

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
            INSERT INTO DE_TASK_CHECKLIST_ITEM (
                TASK_CHECKLIST_ITEM_ID
                , TASK_ITEM_ID
                , LABEL
                , IS_COMPLETED
                , CREATED_DATE
                , UPDATED_DATE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_LOC
                )
            VALUES (
                @Id
                , @TaskId
                , @Label
                , @IsCompleted
                , @CreatedAt
                , @UpdatedAt
                , 1
                , 'SYSTEM'
                , '127.0.0.1'
                , 'SYSTEM'
                , '127.0.0.1'
            );
        ";

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
        const string projectExistsSql = @"
            SELECT COUNT(1)
            FROM MM_PROJECT
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";
        var projectExists = await connection.ExecuteScalarAsync<int>(projectExistsSql, new { ProjectId = projectId }) > 0;
        if (!projectExists)
        {
            return NotFound();
        }

        const string nextPositionSql = @"
            SELECT COALESCE(MAX(POSITION), - 1) + 1
            FROM DE_PROJECT_COLUMN
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var position = await connection.ExecuteScalarAsync<int>(nextPositionSql, new { ProjectId = projectId });
        var column = new ProjectColumn
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            Position = position,
            CreatedAt = DateTime.UtcNow,
        };

        const string insertColumnSql = @"
            INSERT INTO DE_PROJECT_COLUMN (
                PROJECT_COLUMN_ID
                , PROJECT_ID
                , NAME
                , POSITION
                , CREATED_DATE
                , RECORD_TYP
                , CREATED_BY
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_DATE
                , UPDATED_LOC
                )
            VALUES (
                @Id
                , @ProjectId
                , @Name
                , @Position
                , @CreatedAt
                , 1
                , 'SYSTEM'
                , '127.0.0.1'
                , 'SYSTEM'
                , @CreatedAt
                , '127.0.0.1'
            );
        ";

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
            UPDATE DE_PROJECT_COLUMN
            SET NAME = @Name
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_DATE = CURRENT_TIMESTAMP
                , UPDATED_LOC = '127.0.0.1'
            WHERE PROJECT_COLUMN_ID = @ColumnId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1 RETURNING PROJECT_COLUMN_ID AS Id
                , NAME AS Name
                , POSITION AS Position;
        ";

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

        const string orderSql = @"
            SELECT PROJECT_COLUMN_ID
            FROM DE_PROJECT_COLUMN
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var currentColumnIds = (await connection.QueryAsync<Guid>(orderSql, new { ProjectId = projectId })).ToList();
        if (currentColumnIds.Count == 0 && request.ColumnIds.Count == 0)
        {
            return NoContent();
        }
        if (currentColumnIds.Count != request.ColumnIds.Count || !currentColumnIds.All(request.ColumnIds.Contains))
        {
            return BadRequest("The column order must include every column in the project exactly once.");
        }

        await using var transaction = await connection.BeginTransactionAsync();

        const string updatePositionSql = @"
            UPDATE DE_PROJECT_COLUMN
            SET POSITION = @Position
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_DATE = CURRENT_TIMESTAMP
                , UPDATED_LOC = '127.0.0.1'
            WHERE PROJECT_COLUMN_ID = @ColumnId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

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

        const string taskExistsSql = @"
            SELECT COUNT(1)
            FROM DE_TASK_ITEM
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var taskExists = await connection.ExecuteScalarAsync<int>(taskExistsSql, new { TaskId = taskId, ProjectId = projectId }) > 0;
        if (!taskExists)
        {
            return NotFound();
        }

        const string collaboratorSql = @"
            SELECT U.USER_ID AS UserId
                , U.FULL_NAME AS FullName
                , U.EMAIL AS Email
            FROM DE_PROJECT_COLLABORATOR PC
            JOIN MM_USER U ON U.USER_ID = PC.USER_ID
            WHERE PC.PROJECT_ID = @ProjectId
                AND PC.USER_ID = @UserId
                AND PC.STATUS = 1;
        ";

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
            INSERT INTO DE_TASK_ASSIGNEE (
                TASK_ITEM_ID
                , USER_ID
                , STATUS
                , RECORD_TYP
                , CREATED_BY
                , CREATED_DATE
                , CREATED_LOC
                , UPDATED_BY
                , UPDATED_DATE
                , UPDATED_LOC
                )
            VALUES (
                @TaskId
                , @UserId
                , 1
                , 1
                , 'SYSTEM'
                , @AssignedAt
                , '127.0.0.1'
                , 'SYSTEM'
                , @AssignedAt
                , '127.0.0.1'
            ) ON CONFLICT(TASK_ITEM_ID, USER_ID) DO

            UPDATE
            SET STATUS = 1
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_DATE = @AssignedAt
                , UPDATED_LOC = '127.0.0.1';
        ";

        await connection.ExecuteAsync(insertSql, new
        {
            TaskId = taskId,
            UserId = userId,
            AssignedAt = DateTime.UtcNow,
        });

        const string setPrimaryAssigneeSql = @"
            UPDATE DE_TASK_ITEM
            SET ASSIGNED_USER_ID = COALESCE(ASSIGNED_USER_ID, @UserId)
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

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

        const string taskExistsSql = @"
            SELECT COUNT(1)
            FROM DE_TASK_ITEM
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var taskExists = await connection.ExecuteScalarAsync<int>(taskExistsSql, new { TaskId = taskId, ProjectId = projectId }, transaction) > 0;
        if (!taskExists)
        {
            return NotFound();
        }

        const string ownerSql = @"
            SELECT USER_ID
            FROM MM_PROJECT
            WHERE PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var ownerUserId = await connection.ExecuteScalarAsync<Guid?>(ownerSql, new { ProjectId = projectId }, transaction);
        if (ownerUserId == userId)
        {
            return BadRequest("The project owner cannot be removed from a task.");
        }

        const string deleteSql = @"
            UPDATE DE_TASK_ASSIGNEE
            SET STATUS = 0
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_ITEM_ID = @TaskId
                AND USER_ID = @UserId
                AND STATUS = 1;
        ";

        var deleted = await connection.ExecuteAsync(deleteSql, new { TaskId = taskId, UserId = userId, UpdatedAt = DateTime.UtcNow }, transaction);
        if (deleted == 0)
        {
            return NotFound();
        }

        const string updatePrimaryAssigneeSql = @"
            UPDATE DE_TASK_ITEM
            SET ASSIGNED_USER_ID = (
                    SELECT USER_ID
                    FROM DE_TASK_ASSIGNEE
                    WHERE TASK_ITEM_ID = @TaskId
                        AND STATUS = 1
                    ORDER BY CREATED_DATE
                    LIMIT 1
                    )
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

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
            SELECT CI.TASK_CHECKLIST_ITEM_ID AS Id
                , CI.TASK_ITEM_ID AS TaskId
                , CI.LABEL AS Label
                , CI.IS_COMPLETED AS IsCompleted
                , CI.CREATED_DATE AS CreatedAt
                , CI.UPDATED_DATE AS UpdatedAt
            FROM DE_TASK_CHECKLIST_ITEM CI
            JOIN DE_TASK_ITEM TI ON TI.TASK_ITEM_ID = CI.TASK_ITEM_ID
            WHERE CI.TASK_CHECKLIST_ITEM_ID = @ItemId
                AND CI.TASK_ITEM_ID = @TaskId
                AND TI.PROJECT_ID = @ProjectId
                AND CI.STATUS = 1;
        ";

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
            UPDATE DE_TASK_CHECKLIST_ITEM
            SET LABEL = @Label
                , IS_COMPLETED = @IsCompleted
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_CHECKLIST_ITEM_ID = @Id
                AND TASK_ITEM_ID = @TaskId
                AND STATUS = 1;
        ";

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
            UPDATE DE_TASK_ITEM
            SET PROJECT_COLUMN_ID = @ColumnId
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

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

    [HttpPatch("{projectId:guid}/tasks/{taskId:guid}")]
    public async Task<IActionResult> UpdateTaskDetails(Guid projectId, Guid taskId, UpdateTaskDetailsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description)
            || request.ColumnId == Guid.Empty || request.StartDate == null || request.DueDate == null
            || request.Priority == null)
        {
            return BadRequest("Title, description, column, dates, and priority are required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var updatedAt = DateTime.UtcNow;
        var assigneeIds = (request.AssignedUserIds ?? [])
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToList();
        const string updateTaskSql = @"
            UPDATE DE_TASK_ITEM
            SET TITLE = @Title
                , DESCRIPTION = @Description
                , PROJECT_COLUMN_ID = @ColumnId
                , BOARD_TYPE = @BoardType
                , ASSIGNED_USER_ID = @AssignedUserId
                , START_DATE = @StartDate
                , DUE_DATE = @DueDate
                , PRIORITY = @Priority
                , CATEGORY = @Category
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_ITEM_ID = @TaskId
                AND PROJECT_ID = @ProjectId
                AND STATUS = 1;
        ";

        var rows = await connection.ExecuteAsync(updateTaskSql, new
        {
            Title = request.Title?.Trim() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty,
            BoardType = string.IsNullOrWhiteSpace(request.BoardType) ? null : request.BoardType.Trim(),
            request.ColumnId,
            AssignedUserId = assigneeIds.FirstOrDefault() == Guid.Empty ? null : (Guid?)assigneeIds.First(),
            request.StartDate,
            request.DueDate,
            Priority = request.Priority,
            Category = request.TagId?.ToString(),
            UpdatedAt = updatedAt,
            TaskId = taskId,
            ProjectId = projectId,
        }, transaction);
        if (rows == 0)
        {
            return NotFound();
        }

        const string deleteTaskSql = @"
            UPDATE DE_TASK_ASSIGNEE
            SET STATUS = 0
                , UPDATED_BY = 'SYSTEM'
                , UPDATED_DATE = @UpdatedAt
                , UPDATED_LOC = '127.0.0.1'
            WHERE TASK_ITEM_ID = @TaskId
                AND STATUS = 1;
        ";
        // Soft-delete all current assignees for this task, then upsert the new set
        await connection.ExecuteAsync(deleteTaskSql, new { TaskId = taskId, UpdatedAt = updatedAt }, transaction);
        foreach (var userId in assigneeIds)
        {
            const string sql = @"
                INSERT INTO DE_TASK_ASSIGNEE (
                    TASK_ITEM_ID
                    , USER_ID
                    , STATUS
                    , RECORD_TYP
                    , CREATED_BY
                    , CREATED_DATE
                    , CREATED_LOC
                    , UPDATED_BY
                    , UPDATED_DATE
                    , UPDATED_LOC
                    )
                VALUES (
                    @TaskId
                    , @UserId
                    , 1
                    , 1
                    , 'SYSTEM'
                    , @AssignedAt
                    , '127.0.0.1'
                    , 'SYSTEM'
                    , @AssignedAt
                    , '127.0.0.1'
                ) ON CONFLICT(TASK_ITEM_ID, USER_ID) DO

                UPDATE
                SET STATUS = 1
                    , UPDATED_BY = 'SYSTEM'
                    , UPDATED_DATE = @AssignedAt
                    , UPDATED_LOC = '127.0.0.1';
            ";

            await connection.ExecuteAsync(sql, new { TaskId = taskId, UserId = userId, AssignedAt = updatedAt }, transaction);
        }

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
    public string Name { get; set; }
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
    public string Name { get; set; }
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
    public string Name { get; set; }
    public string? Color { get; set; }
}

public class ProjectCollaboratorApiResponse
{
    public Guid UserId { get; set; }
    public string Role { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
}

public sealed class CreateTaskItemRequest
{
    public string Title { get; set; }
    public string? Description { get; set; }
    public string BoardType { get; set; }
    public Guid ColumnId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public List<Guid>? AssignedUserIds { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskPriority Priority { get; set; }
    public Guid? TagId { get; set; }
}

public sealed class UpdateTaskDetailsRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? BoardType { get; set; }
    public Guid ColumnId { get; set; }
    public List<Guid>? AssignedUserIds { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskPriority? Priority { get; set; }
    public Guid? TagId { get; set; }
}

public class TaskItemApiResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public Guid ColumnId { get; set; }
    public string? AssignedUserName { get; set; }
    public List<TaskAssigneeApiResponse> Assignees { get; set; } = [];
    public DateTime? DueDate { get; set; }
    public TaskPriority Priority { get; set; }
    public List<ProjectTagApiResponse> Tags { get; set; } = [];
}

public sealed record AddProjectCollaboratorRequest(Guid UserId, string Role = "member");
public sealed class TaskTagRow
{
    public Guid TaskId { get; init; }
    public Guid TagId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string? Color { get; init; }
}

public sealed class TaskDetailRow
{
    public Guid Id { get; init; }
    public string Title { get; init; }
    public string? Description { get; init; }
    public Guid ProjectId { get; init; }
    public Guid ColumnId { get; init; }
    public string? BoardType { get; init; }
    public Guid? AssignedUserId { get; init; }
    public string? AssignedUserName { get; init; }
    public string? AssignedUserEmail { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? DueDate { get; init; }
    public TaskPriority? Priority { get; init; }
    public string? Category { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? ColumnName { get; init; }
}

public sealed class TaskDetailApiResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; }
    public Guid ColumnId { get; set; }
    public string ColumnName { get; set; }
    public string? BoardType { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string? AssignedUserEmail { get; set; }
    public List<TaskAssigneeApiResponse> Assignees { get; set; } = [];
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskPriority Priority { get; set; }
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
    public string Label { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TaskAttachmentApiResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public string? FileType { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
}

public sealed class TaskAssigneeApiResponse
{
    public Guid UserId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
}

public sealed class CreateChecklistItemRequest
{
    public string Label { get; set; }
}

public sealed class UpdateChecklistItemRequest
{
    public string? Label { get; set; }
    public bool? IsCompleted { get; set; }
}
