using CMS.Data;
using CMS.Services.Auth;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
// using Npgsql;
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
    await connection.ExecuteAsync("""
        IF OBJECT_ID('dbo.MM_USER', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MM_USER
            (
                USER_ID UNIQUEIDENTIFIER NOT NULL,
                FULL_NAME NVARCHAR(255) NOT NULL,
                EMAIL NVARCHAR(255) NOT NULL,
                PASSWORD_HASH NVARCHAR(MAX) NULL,
                PASSWORD_SALT NVARCHAR(MAX) NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_MM_USER_USER_ID
                    PRIMARY KEY (USER_ID)
            );
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'UIX_MM_USER_EMAIL'
            AND object_id = OBJECT_ID('dbo.MM_USER')
        )
        BEGIN
            CREATE UNIQUE INDEX UIX_MM_USER_EMAIL
            ON dbo.MM_USER(EMAIL);
        END;

        IF OBJECT_ID('dbo.MM_PROJECT', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MM_PROJECT
            (
                PROJECT_ID UNIQUEIDENTIFIER NOT NULL,
                USER_ID UNIQUEIDENTIFIER NOT NULL,
                NAME NVARCHAR(255) NOT NULL,
                DESCRIPTION NVARCHAR(MAX) NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_MM_PROJECT_PROJECT_ID
                    PRIMARY KEY (PROJECT_ID),

                CONSTRAINT FK_MM_PROJECT_MM_USER
                    FOREIGN KEY (USER_ID)
                    REFERENCES dbo.MM_USER(USER_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_PROJECT_COLUMN', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_PROJECT_COLUMN
            (
                PROJECT_COLUMN_ID UNIQUEIDENTIFIER NOT NULL,
                PROJECT_ID UNIQUEIDENTIFIER NOT NULL,
                NAME NVARCHAR(100) NOT NULL,
                POSITION INT NOT NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_PROJECT_COLUMN_COLUMN_ID
                    PRIMARY KEY (PROJECT_COLUMN_ID),

                CONSTRAINT FK_DE_PROJECT_COLUMN_MM_PROJECT
                    FOREIGN KEY (PROJECT_ID)
                    REFERENCES dbo.MM_PROJECT(PROJECT_ID)
            );
        END;

        IF OBJECT_ID('dbo.MM_PROJECT_TAG', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MM_PROJECT_TAG
            (
                PROJECT_TAG_ID UNIQUEIDENTIFIER NOT NULL,
                PROJECT_ID UNIQUEIDENTIFIER NOT NULL,
                NAME NVARCHAR(100) NOT NULL,
                COLOR NVARCHAR(MAX) NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_MM_PROJECT_TAG_TAG_ID
                    PRIMARY KEY (PROJECT_TAG_ID),

                CONSTRAINT FK_MM_PROJECT_TAG_MM_PROJECT
                    FOREIGN KEY (PROJECT_ID)
                    REFERENCES dbo.MM_PROJECT(PROJECT_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_PROJECT_COLLABORATOR', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_PROJECT_COLLABORATOR
            (
                PROJECT_ID UNIQUEIDENTIFIER NOT NULL,
                USER_ID UNIQUEIDENTIFIER NOT NULL,
                ROLE NVARCHAR(50) NOT NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_PROJECT_COLLABORATOR
                    PRIMARY KEY (PROJECT_ID, USER_ID),

                CONSTRAINT FK_DE_PROJECT_COLLABORATOR_MM_PROJECT
                    FOREIGN KEY (PROJECT_ID)
                    REFERENCES dbo.MM_PROJECT(PROJECT_ID),

                CONSTRAINT FK_DE_PROJECT_COLLABORATOR_MM_USER
                    FOREIGN KEY (USER_ID)
                    REFERENCES dbo.MM_USER(USER_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_TASK_ITEM', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_TASK_ITEM
            (
                TASK_ITEM_ID UNIQUEIDENTIFIER NOT NULL,
                TITLE NVARCHAR(255) NOT NULL,
                DESCRIPTION NVARCHAR(MAX) NULL,
                BOARD_TYPE NVARCHAR(50) NULL,

                PROJECT_ID UNIQUEIDENTIFIER NOT NULL,
                PROJECT_COLUMN_ID UNIQUEIDENTIFIER NOT NULL,
                ASSIGNED_USER_ID UNIQUEIDENTIFIER NULL,

                START_DATE DATETIME NULL,
                DUE_DATE DATETIME NULL,

                PRIORITY NVARCHAR(50) NOT NULL,
                CATEGORY NVARCHAR(100) NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_TASK_ITEM_TASK_ITEM_ID
                    PRIMARY KEY (TASK_ITEM_ID),

                CONSTRAINT FK_DE_TASK_ITEM_MM_PROJECT
                    FOREIGN KEY (PROJECT_ID)
                    REFERENCES dbo.MM_PROJECT(PROJECT_ID),

                CONSTRAINT FK_DE_TASK_ITEM_DE_PROJECT_COLUMN
                    FOREIGN KEY (PROJECT_COLUMN_ID)
                    REFERENCES dbo.DE_PROJECT_COLUMN(PROJECT_COLUMN_ID),

                CONSTRAINT FK_DE_TASK_ITEM_MM_USER
                    FOREIGN KEY (ASSIGNED_USER_ID)
                    REFERENCES dbo.MM_USER(USER_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_TASK_ITEM_TAG', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_TASK_ITEM_TAG
            (
                TASK_ITEM_ID UNIQUEIDENTIFIER NOT NULL,
                PROJECT_TAG_ID UNIQUEIDENTIFIER NOT NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_TASK_ITEM_TAG
                    PRIMARY KEY (TASK_ITEM_ID, PROJECT_TAG_ID),

                CONSTRAINT FK_DE_TASK_ITEM_TAG_DE_TASK_ITEM
                    FOREIGN KEY (TASK_ITEM_ID)
                    REFERENCES dbo.DE_TASK_ITEM(TASK_ITEM_ID),

                CONSTRAINT FK_DE_TASK_ITEM_TAG_MM_PROJECT_TAG
                    FOREIGN KEY (PROJECT_TAG_ID)
                    REFERENCES dbo.MM_PROJECT_TAG(PROJECT_TAG_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_TASK_ATTACHMENT', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_TASK_ATTACHMENT
            (
                TASK_ATTACHMENT_ID UNIQUEIDENTIFIER NOT NULL,
                TASK_ITEM_ID UNIQUEIDENTIFIER NOT NULL,

                FILE_NAME NVARCHAR(1024) NOT NULL,
                FILE_PATH NVARCHAR(MAX) NOT NULL,
                FILE_TYPE NVARCHAR(255) NULL,
                FILE_SIZE BIGINT NOT NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_TASK_ATTACHMENT_ATTACHMENT_ID
                    PRIMARY KEY (TASK_ATTACHMENT_ID),

                CONSTRAINT FK_DE_TASK_ATTACHMENT_DE_TASK_ITEM
                    FOREIGN KEY (TASK_ITEM_ID)
                    REFERENCES dbo.DE_TASK_ITEM(TASK_ITEM_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_TASK_CHECKLIST_ITEM', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_TASK_CHECKLIST_ITEM
            (
                TASK_CHECKLIST_ITEM_ID UNIQUEIDENTIFIER NOT NULL,
                TASK_ITEM_ID UNIQUEIDENTIFIER NOT NULL,

                LABEL NVARCHAR(500) NOT NULL,
                IS_COMPLETED BIT NOT NULL DEFAULT 0,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_TASK_CHECKLIST_ITEM_CHECKLIST_ID
                    PRIMARY KEY (TASK_CHECKLIST_ITEM_ID),

                CONSTRAINT FK_DE_TASK_CHECKLIST_ITEM_DE_TASK_ITEM
                    FOREIGN KEY (TASK_ITEM_ID)
                    REFERENCES dbo.DE_TASK_ITEM(TASK_ITEM_ID)
            );
        END;

        IF OBJECT_ID('dbo.DE_TASK_ASSIGNEE', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DE_TASK_ASSIGNEE
            (
                TASK_ITEM_ID UNIQUEIDENTIFIER NOT NULL,
                USER_ID UNIQUEIDENTIFIER NOT NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL DEFAULT 'SYSTEM',
                CREATED_DATE DATETIME NOT NULL DEFAULT SYSDATETIME(),
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_DE_TASK_ASSIGNEE
                    PRIMARY KEY (TASK_ITEM_ID, USER_ID),

                CONSTRAINT FK_DE_TASK_ASSIGNEE_DE_TASK_ITEM
                    FOREIGN KEY (TASK_ITEM_ID)
                    REFERENCES dbo.DE_TASK_ITEM(TASK_ITEM_ID),

                CONSTRAINT FK_DE_TASK_ASSIGNEE_MM_USER
                    FOREIGN KEY (USER_ID)
                    REFERENCES dbo.MM_USER(USER_ID)
            );
        END;

        IF OBJECT_ID('dbo.MM_CUSTOMER', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MM_CUSTOMER
            (
                CUSTOMER_ID INT IDENTITY(1,1) NOT NULL,

                NAME NVARCHAR(255) NOT NULL,
                EMAIL NVARCHAR(255) NOT NULL,
                PHONE NVARCHAR(50) NOT NULL,

                ADDRESS NVARCHAR(MAX) NULL,
                CITY NVARCHAR(100) NULL,
                STATE NVARCHAR(100) NULL,
                POSTAL_CODE NVARCHAR(20) NULL,
                COUNTRY NVARCHAR(100) NULL,

                STATUS INT NOT NULL DEFAULT 1,
                RECORD_TYP INT NOT NULL DEFAULT 1,
                CREATED_BY NVARCHAR(50) NOT NULL,
                CREATED_DATE DATETIME NOT NULL,
                CREATED_LOC NVARCHAR(15) NULL,
                UPDATED_BY NVARCHAR(50) NULL,
                UPDATED_DATE DATETIME NULL,
                UPDATED_LOC NVARCHAR(15) NULL,

                CONSTRAINT PK_MM_CUSTOMER_CUSTOMER_ID
                    PRIMARY KEY (CUSTOMER_ID)
            );
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_MM_PROJECT_USER_ID'
            AND object_id = OBJECT_ID('dbo.MM_PROJECT')
        )
        BEGIN
            CREATE INDEX IX_MM_PROJECT_USER_ID
            ON dbo.MM_PROJECT(USER_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_PROJECT_COLUMN_PROJECT_ID'
            AND object_id = OBJECT_ID('dbo.DE_PROJECT_COLUMN')
        )
        BEGIN
            CREATE INDEX IX_DE_PROJECT_COLUMN_PROJECT_ID
            ON dbo.DE_PROJECT_COLUMN(PROJECT_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_MM_PROJECT_TAG_PROJECT_ID'
            AND object_id = OBJECT_ID('dbo.MM_PROJECT_TAG')
        )
        BEGIN
            CREATE INDEX IX_MM_PROJECT_TAG_PROJECT_ID
            ON dbo.MM_PROJECT_TAG(PROJECT_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_PROJECT_COLLABORATOR_USER_ID'
            AND object_id = OBJECT_ID('dbo.DE_PROJECT_COLLABORATOR')
        )
        BEGIN
            CREATE INDEX IX_DE_PROJECT_COLLABORATOR_USER_ID
            ON dbo.DE_PROJECT_COLLABORATOR(USER_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_ITEM_PROJECT_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_ITEM')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_ITEM_PROJECT_ID
            ON dbo.DE_TASK_ITEM(PROJECT_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_ITEM_PROJECT_COLUMN_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_ITEM')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_ITEM_PROJECT_COLUMN_ID
            ON dbo.DE_TASK_ITEM(PROJECT_COLUMN_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_ITEM_ASSIGNED_USER_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_ITEM')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_ITEM_ASSIGNED_USER_ID
            ON dbo.DE_TASK_ITEM(ASSIGNED_USER_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_ITEM_TAG_PROJECT_TAG_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_ITEM_TAG')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_ITEM_TAG_PROJECT_TAG_ID
            ON dbo.DE_TASK_ITEM_TAG(PROJECT_TAG_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_ATTACHMENT_TASK_ITEM_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_ATTACHMENT')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_ATTACHMENT_TASK_ITEM_ID
            ON dbo.DE_TASK_ATTACHMENT(TASK_ITEM_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_CHECKLIST_ITEM_TASK_ITEM_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_CHECKLIST_ITEM')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_CHECKLIST_ITEM_TASK_ITEM_ID
            ON dbo.DE_TASK_CHECKLIST_ITEM(TASK_ITEM_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_DE_TASK_ASSIGNEE_USER_ID'
            AND object_id = OBJECT_ID('dbo.DE_TASK_ASSIGNEE')
        )
        BEGIN
            CREATE INDEX IX_DE_TASK_ASSIGNEE_USER_ID
            ON dbo.DE_TASK_ASSIGNEE(USER_ID);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = 'IX_MM_CUSTOMER_EMAIL'
            AND object_id = OBJECT_ID('dbo.MM_CUSTOMER')
        )
        BEGIN
            CREATE INDEX IX_MM_CUSTOMER_EMAIL
            ON dbo.MM_CUSTOMER(EMAIL);
        END;
""");

    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var userCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM MM_USER;");
    if (userCount == 0)
    {
        // Seed admin user
        var (adminHash, adminSalt) = passwordHasher.HashPassword("Admin123!");
        await connection.ExecuteAsync(
            @"INSERT INTO MM_USER (USER_ID, FULL_NAME, EMAIL, PASSWORD_HASH, PASSWORD_SALT, RECORD_TYP, CREATED_BY, CREATED_DATE, CREATED_LOC)
              VALUES (@Id, @FullName, @Email, @PasswordHash, @PasswordSalt, 1, 'SYSTEM', @CreatedAt, '127.0.0.1');",
            new { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), FullName = "Admin User", Email = "admin@cms.local", PasswordHash = adminHash, PasswordSalt = adminSalt, CreatedAt = DateTime.UtcNow });

        // Seed 10 sample user records
        for (int i = 1; i <= 10; i++)
        {
            var userId = Guid.NewGuid();
            var fullName = $"Sample User {i}";
            var email = $"user{i}@cms.local";
            var (hash, salt) = passwordHasher.HashPassword("Password123!");

            await connection.ExecuteAsync(
                @"INSERT INTO MM_USER (USER_ID, FULL_NAME, EMAIL, PASSWORD_HASH, PASSWORD_SALT, RECORD_TYP, CREATED_BY, CREATED_DATE, CREATED_LOC)
                  VALUES (@Id, @FullName, @Email, @PasswordHash, @PasswordSalt, 1, 'SYSTEM', @CreatedAt, '127.0.0.1');",
                new { Id = userId, FullName = fullName, Email = email, PasswordHash = hash, PasswordSalt = salt, CreatedAt = DateTime.UtcNow });
        }
    }
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

// static bool IsBase64(string? value)
// {
//     if (string.IsNullOrWhiteSpace(value))
//     {
//         return false;
//     }

//     return Convert.TryFromBase64String(value, new byte[value.Length], out _);
// }

file sealed class AdminCredentials
{
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
}
