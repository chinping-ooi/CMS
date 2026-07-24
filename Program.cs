using CMS.Data;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<DapperContext>();

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
        """);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllers();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
