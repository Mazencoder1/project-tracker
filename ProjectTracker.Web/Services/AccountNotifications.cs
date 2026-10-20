namespace ProjectTracker.Web.Services;

/// <summary>"Your account was created" e-mail and SMS (port of sendAccountCreatedNotifications). No password is ever included.</summary>
public sealed class AccountNotifications(IMailService mail, ISmsService sms)
{
    public sealed record Result(
        [property: System.Text.Json.Serialization.JsonPropertyName("email")] NotificationResult Email,
        [property: System.Text.Json.Serialization.JsonPropertyName("sms")] NotificationResult Sms);

    public async Task<Result> SendAccountCreatedAsync(EmployeeRecord employee, CancellationToken ct = default)
    {
        var emailTask = !string.IsNullOrEmpty(employee.Email)
            ? mail.SendAsync(new MailRequest(
                [employee.Email],
                "تم إنشاء حسابك في منصة متابعة المشاريع",
                NotifyTemplates.GreetingCard(employee.FullName,
                    "<p>تم إنشاء حسابك في منصة متابعة المشاريع بإدارة المشاريع.</p>"
                    + "<p>يمكنك تسجيل الدخول ببريدك الإلكتروني وكلمة المرور التي يزوّدك بها مدير النظام.</p>"
                    + NotifyTemplates.FinePrint("لا تتضمن هذه الرسالة كلمة المرور حفاظًا على أمان حسابك."))), ct)
            : Task.FromResult(NotificationResult.NotConfigured("لا يوجد بريد للموظف."));

        var smsTask = !string.IsNullOrEmpty(employee.MobileNo)
            ? sms.SendAsync(employee.MobileNo, "تم إنشاء حسابك في منصة متابعة المشاريع بإدارة المشاريع. يمكنك الدخول ببريدك الإلكتروني.", ct)
            : Task.FromResult(NotificationResult.NotConfigured("لا يوجد رقم جوال للموظف."));

        await Task.WhenAll(emailTask, smsTask);
        return new Result(await emailTask, await smsTask);
    }
}
