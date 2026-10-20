using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Controllers;

/// <summary>قائمة الإدارات لحقل «الإدارة المالكة» في إنشاء المشروع وتعديله (مصدرها الإعدادات).</summary>
[Route("api/departments")]
public sealed class DepartmentsController(DepartmentService departments) : ApiControllerBase
{
    [Authorize(Policy = TrackerAuth.AdminOrDeputyPolicy)]
    [HttpGet("")]
    public IActionResult List() => Json(new
    {
        source = "config",
        departments = departments.GetDepartments().Select(d => new { id = d.Id, name = d.Name }),
    });
}
