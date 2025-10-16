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

    public virtual DbSet<Subcontractor> Subcontractors { get; set; }

    public virtual DbSet<SubcontractorAttachmentsMapping> SubcontractorAttachmentsMappings { get; set; }

    public virtual DbSet<SubcontractorWorkItemMapping> SubcontractorWorkItemMappings { get; set; }

    public virtual DbSet<WorkItem> WorkItems { get; set; }

    public virtual DbSet<WorkItemCategoryType> WorkItemCategoryTypes { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=10.100.0.44\\SQLSERVER2022;Database=UnibouwQMS_Dev;User Id=UnibouwQMS;Password=Un!b0uwQMS;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
            entity.Property(e => e.RegisteredDate).HasMaxLength(255);
            entity.Property(e => e.WorkItemsId).HasColumnName("WorkItemsID");

            entity.HasOne(d => d.Attachments).WithMany(p => p.Subcontractors)
                .HasForeignKey(d => d.AttachmentsId)
                .HasConstraintName("FK_Subcontractor_AttachmentsID");

            entity.HasOne(d => d.WorkItems).WithMany(p => p.Subcontractors)
                .HasForeignKey(d => d.WorkItemsId)
                .HasConstraintName("FK_Subcontractor_WorkItemsID");
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

            entity.HasOne(d => d.Category).WithMany(p => p.SubcontractorWorkItemMappingCategories)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("FK_Categories_Category");

            entity.HasOne(d => d.WorkItem).WithMany(p => p.SubcontractorWorkItemMappingWorkItems)
                .HasForeignKey(d => d.WorkItemId)
                .HasConstraintName("FK_WorkItems_WorkItem");
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

            /*entity.HasOne(d => d.Category).WithMany(p => p.WorkItems)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_WorkItems_Category");*/
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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
