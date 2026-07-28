using CMS.Data;
using CMS.Services.Auth;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<DapperContext>();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token)
                    && context.Request.Cookies.TryGetValue("cms_token", out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api"))
                {
                    context.HandleResponse();
                    context.Response.Redirect("/login");
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    // All application routes require a valid JWT unless explicitly marked
    // with [AllowAnonymous], such as the login page and auth endpoints.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddHttpClient();

builder.Services.AddHttpClient(
    "localhost",
    client =>
    {
        client.BaseAddress = new Uri("http://localhost:5001/");
    }
);

builder.Services.AddControllersWithViews();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dapperContext = scope.ServiceProvider.GetRequiredService<DapperContext>();
    await using var connection = await dapperContext.CreateOpenConnectionAsync();
    await connection.ExecuteAsync(
        """
        CREATE TABLE IF NOT EXISTS project (
            id uuid NOT NULL PRIMARY KEY,
            name character varying(255) NOT NULL,
            description text NULL,
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL
        );

        CREATE TABLE IF NOT EXISTS users (
            id uuid NOT NULL PRIMARY KEY,
            full_name character varying(255) NOT NULL,
            email character varying(255) NOT NULL,
            password_hash text NULL,
            password_salt text NULL,
            created_at timestamp with time zone NOT NULL
        );

        CREATE TABLE IF NOT EXISTS project_column (
            id uuid NOT NULL PRIMARY KEY,
            project_id uuid NOT NULL,
            name character varying(100) NOT NULL,
            position integer NOT NULL,
            created_at timestamp with time zone NOT NULL,
            CONSTRAINT fk_project_column_project FOREIGN KEY (project_id)
                REFERENCES project (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS project_tag (
            id uuid NOT NULL PRIMARY KEY,
            project_id uuid NOT NULL,
            name character varying(100) NOT NULL,
            color text NULL,
            created_at timestamp with time zone NOT NULL,
            CONSTRAINT fk_project_tag_project FOREIGN KEY (project_id)
                REFERENCES project (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS project_collaborator (
            project_id uuid NOT NULL,
            user_id uuid NOT NULL,
            role text NOT NULL,
            joined_at timestamp with time zone NOT NULL,
            CONSTRAINT pk_project_collaborator PRIMARY KEY (project_id, user_id),
            CONSTRAINT fk_project_collaborator_project FOREIGN KEY (project_id)
                REFERENCES project (id) ON DELETE CASCADE,
            CONSTRAINT fk_project_collaborator_user FOREIGN KEY (user_id)
                REFERENCES users (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS task_item (
            id uuid NOT NULL PRIMARY KEY,
            title character varying(255) NOT NULL,
            description text NULL,
            board_type text NULL,
            project_id uuid NOT NULL,
            column_id uuid NOT NULL,
            assigned_user_id uuid NULL,
            start_date timestamp with time zone NULL,
            due_date timestamp with time zone NULL,
            priority text NOT NULL,
            category text NULL,
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL,
            CONSTRAINT fk_task_item_project FOREIGN KEY (project_id)
                REFERENCES project (id) ON DELETE CASCADE,
            CONSTRAINT fk_task_item_column FOREIGN KEY (column_id)
                REFERENCES project_column (id) ON DELETE CASCADE,
            CONSTRAINT fk_task_item_user FOREIGN KEY (assigned_user_id)
                REFERENCES users (id)
        );

        -- Ensure priority column is text if previously integer
        -- and add new optional columns when upgrading an existing schema.
        ALTER TABLE task_item
            ADD COLUMN IF NOT EXISTS board_type text NULL;
        ALTER TABLE task_item
            ADD COLUMN IF NOT EXISTS start_date timestamp with time zone NULL;
        ALTER TABLE task_item
            ADD COLUMN IF NOT EXISTS category text NULL;
        -- Attempt to convert existing priority column to text (harmless if already text)
        ALTER TABLE task_item
            ALTER COLUMN priority TYPE text USING priority::text;

        CREATE TABLE IF NOT EXISTS task_item_tag (
            task_id uuid NOT NULL,
            tag_id uuid NOT NULL,
            CONSTRAINT pk_task_item_tag PRIMARY KEY (task_id, tag_id),
            CONSTRAINT fk_task_item_tag_task FOREIGN KEY (task_id)
                REFERENCES task_item (id) ON DELETE CASCADE,
            CONSTRAINT fk_task_item_tag_tag FOREIGN KEY (tag_id)
                REFERENCES project_tag (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS task_attachment (
            id uuid NOT NULL PRIMARY KEY,
            task_id uuid NOT NULL,
            file_name character varying(1024) NOT NULL,
            file_path text NOT NULL,
            file_type character varying(255) NULL,
            file_size bigint NOT NULL,
            uploaded_at timestamp with time zone NOT NULL,
            CONSTRAINT fk_task_attachment_task FOREIGN KEY (task_id)
                REFERENCES task_item (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS task_checklist_item (
            id uuid NOT NULL PRIMARY KEY,
            task_id uuid NOT NULL,
            label character varying(500) NOT NULL,
            is_completed boolean NOT NULL DEFAULT false,
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL,
            CONSTRAINT fk_task_checklist_item_task FOREIGN KEY (task_id)
                REFERENCES task_item (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS task_assignee (
            task_id uuid NOT NULL,
            user_id uuid NOT NULL,
            assigned_at timestamp with time zone NOT NULL,
            CONSTRAINT pk_task_assignee PRIMARY KEY (task_id, user_id),
            CONSTRAINT fk_task_assignee_task FOREIGN KEY (task_id)
                REFERENCES task_item (id) ON DELETE CASCADE,
            CONSTRAINT fk_task_assignee_user FOREIGN KEY (user_id)
                REFERENCES users (id) ON DELETE CASCADE
        );
        """);

    await connection.ExecuteAsync("ALTER TABLE task_item ADD COLUMN IF NOT EXISTS board_type text NULL;");

    await connection.ExecuteAsync(
        """
        ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash text NULL;
        ALTER TABLE users ADD COLUMN IF NOT EXISTS password_salt text NULL;
        ALTER TABLE project ADD COLUMN IF NOT EXISTS user_id uuid NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email_lower ON users (LOWER(email));
        """);

    await connection.ExecuteAsync(
        """
        INSERT INTO task_assignee (task_id, user_id, assigned_at)
        SELECT ti.id, ti.assigned_user_id, ti.created_at
        FROM task_item ti
        WHERE ti.assigned_user_id IS NOT NULL
        ON CONFLICT (task_id, user_id) DO NOTHING;
        """);

    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var adminExists = await connection.ExecuteScalarAsync<int>(
        "SELECT COUNT(1) FROM users WHERE LOWER(email) = LOWER(@Email)",
        new { Email = "admin@cms.local" });

    if (adminExists == 0)
    {
        var (hash, salt) = passwordHasher.HashPassword("Admin123!");
        const string insertAdminSql = @"
            INSERT INTO users (id, full_name, email, password_hash, password_salt, created_at)
            VALUES (@Id, @FullName, @Email, @PasswordHash, @PasswordSalt, @CreatedAt)";

        await connection.ExecuteAsync(insertAdminSql, new
        {
            Id = Guid.NewGuid(),
            FullName = "Admin User",
            Email = "admin@cms.local",
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAt = DateTime.UtcNow,
        });
    }
    else
    {
        const string adminCredentialsSql = @"
            SELECT password_hash AS PasswordHash, password_salt AS PasswordSalt
            FROM users
            WHERE LOWER(email) = LOWER(@Email)
            LIMIT 1";

        var credentials = await connection.QuerySingleAsync<AdminCredentials>(
            adminCredentialsSql,
            new { Email = "admin@cms.local" });

        if (!IsBase64(credentials.PasswordHash) || !IsBase64(credentials.PasswordSalt))
        {
            var (hash, salt) = passwordHasher.HashPassword("Admin123!");
            await connection.ExecuteAsync(
                "UPDATE users SET password_hash = @Hash, password_salt = @Salt WHERE LOWER(email) = LOWER(@Email)",
                new { Hash = hash, Salt = salt, Email = "admin@cms.local" });
        }
    }

    await connection.ExecuteAsync(
        """
        UPDATE project p
        SET user_id = collaborator.user_id
        FROM (
            SELECT DISTINCT ON (project_id) project_id, user_id
            FROM project_collaborator
            ORDER BY project_id, joined_at, user_id
        ) collaborator
        WHERE p.id = collaborator.project_id
          AND p.user_id IS NULL;

        UPDATE project
        SET user_id = (
            SELECT id
            FROM users
            WHERE LOWER(email) = LOWER('admin@cms.local')
            LIMIT 1
        )
        WHERE user_id IS NULL;

        ALTER TABLE project ALTER COLUMN user_id SET NOT NULL;

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'fk_project_user'
            ) THEN
                ALTER TABLE project
                    ADD CONSTRAINT fk_project_user
                    FOREIGN KEY (user_id) REFERENCES users (id);
            END IF;
        END $$;
        """);
}

app.UseExceptionHandler("/error");
app.UseStatusCodePagesWithReExecute("/not-found");

if (!app.Environment.IsDevelopment())
{
    // app.UseExceptionHandler("/not-found");
    // app.UseStatusCodePagesWithReExecute("/not-found");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();

app.MapControllers();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

static bool IsBase64(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    return Convert.TryFromBase64String(value, new byte[value.Length], out _);
}

file sealed class AdminCredentials
{
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
}
