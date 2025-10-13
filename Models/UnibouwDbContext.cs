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

    public virtual DbSet<WorkItem> WorkItems { get; set; }

    public virtual DbSet<WorkItemCategoryType> WorkItemCategoryTypes { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=10.100.0.44\\SQLSERVER2022;Database=UnibouwQMS_Dev;User Id=UnibouwQMS;Password=Un!b0uwQMS;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__WorkItem__3214EC273704D4C9");

            entity.Property(e => e.Id).ValueGeneratedNever();

            //entity.HasOne(d => d.Category).WithMany(p => p.WorkItems).HasConstraintName("FK_WorkItems_CategoryID");
        });

        modelBuilder.Entity<WorkItemCategoryType>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__WorkItem__19093A2BB0E73124");

            entity.Property(e => e.CategoryId).ValueGeneratedNever();
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
