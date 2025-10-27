using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace UnibouwAPI.Models;

public partial class UnibouwDbContext : DbContext
{
    public UnibouwDbContext()
    {
    }

    public UnibouwDbContext(DbContextOptions<UnibouwDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<Person> Persons { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectManager> ProjectManagers { get; set; }

    public virtual DbSet<Subcontractor> Subcontractors { get; set; }

    public virtual DbSet<SubcontractorAttachmentsMapping> SubcontractorAttachmentsMappings { get; set; }

    public virtual DbSet<SubcontractorWorkItemMapping> SubcontractorWorkItemMappings { get; set; }

    public virtual DbSet<WorkItem> WorkItems { get; set; }

    public virtual DbSet<WorkItemCategoryType> WorkItemCategoryTypes { get; set; }

    public virtual DbSet<WorkItemsLocal> WorkItemsLocals { get; set; }

    public virtual DbSet<WorkPlanner> WorkPlanners { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=10.100.0.44\\SQLSERVER2022;Database=UnibouwQMS_Dev;User Id=UnibouwQMS;Password=Un!b0uwQMS;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__Customer__A4AE64B8E1A28F1B");

            entity.Property(e => e.CustomerId)
                .ValueGeneratedNever()
                .HasColumnName("CustomerID");
            entity.Property(e => e.Name).HasMaxLength(255);
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasKey(e => e.PersonId).HasName("PK__Person__AA2FFB85A50A65BC");

            entity.Property(e => e.PersonId)
                .ValueGeneratedNever()
                .HasColumnName("PersonID");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.Country).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.DeletedBy).HasMaxLength(50);
            entity.Property(e => e.ErpId).HasColumnName("ERP_ID");
            entity.Property(e => e.Mail).HasMaxLength(100);
            entity.Property(e => e.ModifiedBy).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.PhoneNumber1).HasMaxLength(20);
            entity.Property(e => e.PhoneNumber2).HasMaxLength(20);
            entity.Property(e => e.PostalCode).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(100);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.ProjectId).HasName("PK__Project__761ABED09634786F");

            entity.Property(e => e.ProjectId)
                .ValueGeneratedNever()
                .HasColumnName("ProjectID");
            entity.Property(e => e.Company).HasMaxLength(255);
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.DeletedBy).HasMaxLength(50);
            entity.Property(e => e.ErpId).HasColumnName("ERP_ID");
            entity.Property(e => e.ModifiedBy).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.Number).HasMaxLength(255);
            entity.Property(e => e.PersonId).HasColumnName("PersonID");
            entity.Property(e => e.ProjectMangerId).HasColumnName("ProjectMangerID");
            entity.Property(e => e.SharepointUrl)
                .HasMaxLength(255)
                .HasColumnName("SharepointURL");
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.TotalDimension).HasMaxLength(50);
            entity.Property(e => e.WorkPlannerId).HasColumnName("WorkPlannerID");

            entity.HasOne(d => d.Customer).WithMany(p => p.Projects)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_Project_Customer");

            entity.HasOne(d => d.Person).WithMany(p => p.Projects)
                .HasForeignKey(d => d.PersonId)
                .HasConstraintName("FK_Project_Person");

            entity.HasOne(d => d.ProjectManger).WithMany(p => p.Projects)
                .HasForeignKey(d => d.ProjectMangerId)
                .HasConstraintName("FK_Project_ProjectManager");

            entity.HasOne(d => d.WorkPlanner).WithMany(p => p.Projects)
                .HasForeignKey(d => d.WorkPlannerId)
                .HasConstraintName("FK_Project_WorkPlanner");
        });

        modelBuilder.Entity<ProjectManager>(entity =>
        {
            entity.HasKey(e => e.ProjectManagerId).HasName("PK__ProjectM__35F031F10F92A258");

            entity.Property(e => e.ProjectManagerId)
                .ValueGeneratedNever()
                .HasColumnName("ProjectManagerID");
            entity.Property(e => e.Name).HasMaxLength(255);
        });

        modelBuilder.Entity<Subcontractor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Subcontr__3214EC2777F67784");

            entity.ToTable("Subcontractor");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("ID");
            entity.Property(e => e.AttachmentsId).HasColumnName("AttachmentsID");
            entity.Property(e => e.BillingAddress).HasMaxLength(255);
            entity.Property(e => e.ContactPerson).HasMaxLength(255);
            entity.Property(e => e.Country).HasMaxLength(255);
            entity.Property(e => e.CreatedBy).HasMaxLength(255);
            entity.Property(e => e.DeletedBy).HasMaxLength(255);
            entity.Property(e => e.EmailId)
                .HasMaxLength(255)
                .HasColumnName("EmailID");
            entity.Property(e => e.ErpId)
                .HasMaxLength(255)
                .HasColumnName("ERP_ID");
            entity.Property(e => e.Location).HasMaxLength(255);
            entity.Property(e => e.ModifiedBy).HasMaxLength(255);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.OfficeAdress).HasMaxLength(255);
            entity.Property(e => e.PhoneNumber1).HasColumnType("numeric(18, 0)");
            entity.Property(e => e.PhoneNumber2).HasColumnType("numeric(18, 0)");
            entity.Property(e => e.Rating).HasColumnType("decimal(3, 2)");
            entity.Property(e => e.WorkItemsId).HasColumnName("WorkItemsID");

            entity.HasOne(d => d.Attachments).WithMany(p => p.Subcontractors)
                .HasForeignKey(d => d.AttachmentsId)
                .HasConstraintName("FK_Subcontractor_AttachmentsID");
        });

        modelBuilder.Entity<SubcontractorAttachmentsMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Subcontr__3214EC2771E11BCE");

            entity.ToTable("SubcontractorAttachmentsMapping");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("ID");
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.FileType).HasMaxLength(100);
            entity.Property(e => e.UploadedBy).HasMaxLength(255);
        });

        modelBuilder.Entity<SubcontractorWorkItemMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Subcontr__3214EC2767D70A2D");

            entity.ToTable("SubcontractorWorkItemMapping");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("ID");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.WorkItemId).HasColumnName("WorkItemID");

            entity.HasOne(d => d.Category).WithMany(p => p.SubcontractorWorkItemMappings)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("FK_Categories_Category");

            entity.HasOne(d => d.Subcontractor).WithMany(p => p.SubcontractorWorkItemMappings)
                .HasForeignKey(d => d.SubcontractorId)
                .HasConstraintName("FK_Subcontractor_WorkItemMapping");

            entity.HasOne(d => d.WorkItem).WithMany(p => p.SubcontractorWorkItemMappings)
                .HasForeignKey(d => d.WorkItemId)
                .HasConstraintName("FK_WorkItem_WorkItemMapping");
        });

        modelBuilder.Entity<WorkItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__WorkItem__3214EC27C6E340EB");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("ID");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.DeletedBy).HasMaxLength(100);
            entity.Property(e => e.ErpId)
                .HasMaxLength(50)
                .HasColumnName("ERP_ID");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModifiedBy).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Number).HasMaxLength(50);
            entity.Property(e => e.WorkitemParentId).HasColumnName("WorkitemParent_ID");

            entity.HasOne(d => d.Category).WithMany(p => p.WorkItems)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_WorkItems_Category");
        });

        modelBuilder.Entity<WorkItemCategoryType>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__WorkItem__19093A2BB0E73124");

            entity.ToTable("WorkItemCategoryType");

            entity.Property(e => e.CategoryId)
                .ValueGeneratedNever()
                .HasColumnName("CategoryID");
            entity.Property(e => e.CategoryName).HasMaxLength(255);
        });

        modelBuilder.Entity<WorkItemsLocal>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__WorkItem__3214EC2778835325");

            entity.ToTable("WorkItemsLocal");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("ID");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasPrecision(0)
                .HasColumnName("deleted_at");
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.Number).HasMaxLength(255);
            entity.Property(e => e.Type).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkItemParentId).HasColumnName("WorkItemParent_ID");
        });

        modelBuilder.Entity<WorkPlanner>(entity =>
        {
            entity.HasKey(e => e.WorkPlannerId).HasName("PK__WorkPlan__E6C2E7880CAAF93A");

            entity.Property(e => e.WorkPlannerId)
                .ValueGeneratedNever()
                .HasColumnName("WorkPlannerID");
            entity.Property(e => e.Name).HasMaxLength(255);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
