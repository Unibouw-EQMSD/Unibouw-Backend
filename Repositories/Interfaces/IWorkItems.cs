﻿using UnibouwAPI.Models;

public interface IWorkItems
{
    Task<IEnumerable<WorkItem>> GetAllWorkItems();
    Task<WorkItem?> GetWorkItemById(Guid id);
    Task<int> UpdateWorkItemIsActive(Guid id, bool isActive, string modifiedBy);
    Task<int> UpdateWorkItemDescription(Guid id, string description, string modifiedBy);
}
