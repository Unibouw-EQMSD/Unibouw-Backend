using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace UnibouwAPI.Repositories
{
    public class SubcontractorRepository : ISubcontractor
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public SubcontractorRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<Subcontractor>> GetAllAsync()
        {
            var query = @"
     SELECT
    s.Id AS Id,
    s.ERP_ID AS ErpId,
    s.Name,
    s.Rating,
    s.ContactPerson,
    s.EmailID AS EmailId,
    s.PhoneNumber1,
    s.PhoneNumber2,
    s.Location,
    s.Country,
    s.OfficeAdress,
    s.BillingAddress,
    s.RegisteredDate,
    s.AttachmentsID AS AttachmentsId,
    s.IsActive,
    s.CreatedOn,
    s.CreatedBy,
    s.ModifiedOn,
    s.ModifiedBy,
    s.IsDeleted,
    s.DeletedOn,
    s.DeletedBy,
    COALESCE(STRING_AGG(w.Name, ', '), '') AS Category
FROM Subcontractor s
LEFT JOIN SubcontractorWorkItemMapping m
    ON s.Id = m.SubcontractorId
LEFT JOIN WorkItems w
    ON m.WorkItemID = w.Id
WHERE s.IsDeleted = 0
GROUP BY 
    s.Id, s.ERP_ID, s.Name, s.Rating, s.ContactPerson, s.EmailID,
    s.PhoneNumber1, s.PhoneNumber2, s.Location, s.Country,
    s.OfficeAdress, s.BillingAddress, s.RegisteredDate, s.AttachmentsID,
    s.IsActive, s.CreatedOn, s.CreatedBy, s.ModifiedOn, s.ModifiedBy,
    s.IsDeleted, s.DeletedOn, s.DeletedBy
ORDER BY s.Name;
"; // optional: filter out deleted subcontractors

            return await _connection.QueryAsync<Subcontractor>(query);
        }

        public async Task<Subcontractor?> GetByIdAsync(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<Subcontractor>("SELECT * FROM Subcontractor WHERE ID = @Id", new { Id = id });
        }

        public async Task<int> UpdateIsActiveAsync(Guid id, bool isActive, string modifiedBy)
        {
            // Check if WorkItem is non-deleted WorkItem
            var subcontractor = await _connection.QueryFirstOrDefaultAsync<Subcontractor>(
                "SELECT * FROM Subcontractor WHERE Id = @Id AND IsDeleted = 0",
                new { Id = id }
            );

            // Return 0 if not found (either inactive or deleted)
            if (subcontractor == null)
                return 0;

            var query = @"
        UPDATE Subcontractor SET
            IsActive = @IsActive,
            ModifiedOn = @ModifiedOn,
            ModifiedBy = @ModifiedBy
        WHERE ID = @Id";

            var parameters = new
            {
                Id = id,
                IsActive = isActive,
                ModifiedOn = DateTime.UtcNow,
                ModifiedBy = modifiedBy
            };

            return await _connection.ExecuteAsync(query, parameters);
        }
        public async Task<int> CreateAsync(Subcontractor subcontractor)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
                // 1️⃣ Assign defaults
                subcontractor.Id = subcontractor.Id == Guid.Empty ? Guid.NewGuid() : subcontractor.Id;
                subcontractor.CreatedOn ??= DateTime.UtcNow;
                subcontractor.IsActive ??= true;
                subcontractor.IsDeleted ??= false;
                subcontractor.DeletedOn = subcontractor.IsDeleted == true ? DateTime.UtcNow : null;

                // --- Handle Attachments ---
                if (subcontractor.Attachments == null)
                {
                    subcontractor.Attachments = new SubcontractorAttachmentsMapping
                    {
                        Id = Guid.NewGuid(),
                        FileName = "N/A",
                        FileType = "N/A",
                        FilePath = "N/A",
                        UploadedOn = DateTime.UtcNow,
                        UploadedBy = subcontractor.CreatedBy
                    };
                }
                else
                {
                    subcontractor.Attachments.Id = subcontractor.Attachments.Id == Guid.Empty
                        ? Guid.NewGuid()
                        : subcontractor.Attachments.Id;
                    subcontractor.Attachments.UploadedOn ??= DateTime.UtcNow;
                }

                subcontractor.AttachmentsId = subcontractor.Attachments.Id;

                // 2️⃣ Insert Attachments
                const string insertAttachment = @"
INSERT INTO SubcontractorAttachmentsMapping
(Id, FileName, FileType, FilePath, UploadedOn, UploadedBy)
VALUES (@Id, @FileName, @FileType, @FilePath, @UploadedOn, @UploadedBy)";

                await conn.ExecuteAsync(insertAttachment, new
                {
                    subcontractor.Attachments.Id,
                    subcontractor.Attachments.FileName,
                    subcontractor.Attachments.FileType,
                    subcontractor.Attachments.FilePath,
                    subcontractor.Attachments.UploadedOn,
                    subcontractor.Attachments.UploadedBy
                }, transaction);

                // 3️⃣ Prepare WorkItemsId and Category for Subcontractor
                if (subcontractor.SubcontractorWorkItemMappings != null && subcontractor.SubcontractorWorkItemMappings.Any())
                {
                    // WorkItemsId → comma-separated
                    subcontractor.WorkItemsId = string.Join(",", subcontractor.SubcontractorWorkItemMappings.Select(w => w.WorkItemId));

                    // Category → concatenate work item names from WorkItems table
                    var workItemIds = subcontractor.SubcontractorWorkItemMappings.Select(w => w.WorkItemId).ToList();
                    var categoriesQuery = @"SELECT Id, Name FROM WorkItems WHERE Id IN @Ids";
                    var workItems = (await conn.QueryAsync<WorkItem>(categoriesQuery, new { Ids = workItemIds }, transaction)).ToList();

                    subcontractor.Category = string.Join(", ", subcontractor.SubcontractorWorkItemMappings
                        .Select(wm => workItems.FirstOrDefault(w => w.Id == wm.WorkItemId)?.Name)
                        .Where(n => n != null));
                }
                else
                {
                    throw new Exception("At least one WorkItem mapping is required.");
                }

                // 4️⃣ Insert Subcontractor
                const string insertSubcontractor = @"
INSERT INTO Subcontractor
(Id, ERP_ID, Name, Rating, ContactPerson, EmailId, PhoneNumber1, PhoneNumber2,
 Location, Country, OfficeAdress, BillingAddress, RegisteredDate,
 AttachmentsId, WorkItemsId, IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy,isDeleted,DeletedOn,DeletedBy, Category)
VALUES
(@Id, @ErpId, @Name, @Rating, @ContactPerson, @EmailId, @PhoneNumber1, @PhoneNumber2,
 @Location, @Country, @OfficeAdress, @BillingAddress, @RegisteredDate,
 @AttachmentsId, @WorkItemsId, @IsActive, @CreatedOn, @CreatedBy,@ModifiedOn,@ModifiedBy,@isDeleted,@DeletedOn,@DeletedBy, @Category)";

                await conn.ExecuteAsync(insertSubcontractor, subcontractor, transaction);

                // 5️⃣ Insert WorkItem mappings
                foreach (var workItemMapping in subcontractor.SubcontractorWorkItemMappings)
                {
                    if (workItemMapping.WorkItemId == null || workItemMapping.CategoryId == null)
                        throw new Exception("WorkItemId and CategoryId are required.");

                    workItemMapping.Id = Guid.NewGuid();
                    workItemMapping.SubcontractorId = subcontractor.Id;

                    const string insertWorkItemMapping = @"
INSERT INTO SubcontractorWorkItemMapping
(Id, SubcontractorId, WorkItemID, CategoryID)
VALUES (@Id, @SubcontractorId, @WorkItemId, @CategoryId)";

                    await conn.ExecuteAsync(insertWorkItemMapping, new
                    {
                        workItemMapping.Id,
                        workItemMapping.SubcontractorId,
                        workItemMapping.WorkItemId,
                        workItemMapping.CategoryId
                    }, transaction);
                }

                transaction.Commit();
                return 1;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"Error inserting subcontractor: {ex.Message}", ex);
            }
        }


    }





}

