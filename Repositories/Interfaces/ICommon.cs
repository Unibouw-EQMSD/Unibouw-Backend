using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface ICommon
    {
        Task<IEnumerable<WorkItemCategoryType>> GetAllWorkItemCategoryTypes();
        Task<WorkItemCategoryType?> GetWorkItemCategoryTypeById(Guid id);

        Task<IEnumerable<Person>> GetAllPerson();
        Task<Person?> GetPersonById(Guid id);
        Task<int> CreatePerson(Person person);

        Task<IEnumerable<SubcontractorWorkItemMapping>> GetAllSubcontractorWorkItemMapping(bool onlyActive = false);
        Task<List<SubcontractorWorkItemMapping?>> GetSubcontractorWorkItemMappingById(Guid id);
        Task<bool> CreateSubcontractorWorkItemMapping(SubcontractorWorkItemMapping mapping);

        Task<IEnumerable<SubcontractorAttachmentMapping>> GetAllSubcontractorAttachmentMapping();
        Task<List<SubcontractorAttachmentMapping?>> GetSubcontractorAttachmentMappingById(Guid id);
        // New method for multi-file upload
        Task<bool> CreateSubcontractorAttachmentMappingsAsync(SubcontractorAttachmentMapping model);


        Task<IEnumerable<Customer>> GetAllCustomer();
        Task<Customer?> GetCustomerById(Guid id);

        Task<IEnumerable<RfqResponseStatus>> GetAllRfqResponseStatus();
        Task<RfqResponseStatus?> GetRfqResponseStatusById(Guid id);

        Task<IEnumerable<RfqGlobalReminder>> GetRfqGlobalReminder();
        Task<int> SaveRfqGlobalReminder(RfqGlobalReminder reminder);
    }
}
