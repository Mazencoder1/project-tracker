namespace ProjectTracker.Web.Services.Email;

/// <summary>
/// «اكتمال المشروع» كان يُرسل فقط عند تغيير الحالة يدويًا إلى completed من الإدارة. نسبة الإنجاز
/// تُحسب من المهام، فوصول المشروع إلى 100% بتحديث المهام لم يكن يرسل شيئًا. بعد حفظ/حذف مهمة يُفحص المشروع في
/// الخلفية (لا يؤخر رد الطلب)، وإن بلغ إنجازه الفعلي 100% يُرسل قالب ProjectCompleted لكل مسؤول — مرة واحدة دائمًا
/// (نفس CorrelationKey المستعمل عند الإكمال اليدوي، فلا تكرار بين المسارين).
/// </summary>
public sealed class ProjectCompletionWatcher(IServiceScopeFactory scopes, ILogger<ProjectCompletionWatcher> logger)
{
    public void QueueCheck(string? projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var built = await scope.ServiceProvider.GetRequiredService<PortfolioQueries>().LoadBuiltProjectAsync(projectId);
                if (built is null || built.Project.status == "cancelled" || built.Project.actualProgress is not >= 100) return;
                await scope.ServiceProvider.GetRequiredService<IProjectEmailNotifier>().NotifyProjectCompletedAsync(projectId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The 100% completion check failed for project {Project}", projectId);
            }
        });
    }
}
