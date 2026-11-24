using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface IProjectService
{
    object List(ProjectStatus? status);
    (object? data, string? code, string? message) Create(CurrentUser user, CreateProjectRequest request);
    (object? data, string? code, string? message) AddTask(CurrentUser user, string projectId, CreateTaskRequest request);
    (object? data, string? code, string? message) CompleteTask(CurrentUser user, string projectId, string taskId, decimal hoursActual);
    (object? data, string? code, string? message) Close(CurrentUser closer, string projectId);
}
