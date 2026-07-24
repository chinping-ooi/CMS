using CMS.Models;
using Microsoft.EntityFrameworkCore;

namespace CMS.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<Customer> Customers { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<ProjectColumn> ProjectColumns { get; set; }
    public DbSet<ProjectTag> ProjectTags { get; set; }
    public DbSet<ProjectCollaborator> ProjectCollaborators { get; set; }
    public DbSet<TaskItem> TaskItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>().ToTable("customer");
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<Project>().ToTable("project");
        modelBuilder.Entity<ProjectColumn>().ToTable("project_column");
        modelBuilder.Entity<ProjectTag>().ToTable("project_tag");
        modelBuilder.Entity<ProjectCollaborator>().ToTable("project_collaborator");
        modelBuilder.Entity<TaskItem>().ToTable("task_item");
        modelBuilder.Entity<TaskItemTag>().ToTable("task_item_tag");

        modelBuilder.Entity<ProjectCollaborator>().HasKey(collaborator => new
        {
            collaborator.ProjectId,
            collaborator.UserId,
        });

        modelBuilder.Entity<TaskItemTag>().HasKey(taskTag => new
        {
            taskTag.TaskId,
            taskTag.TagId,
        });
    }
}
