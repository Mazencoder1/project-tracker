using ProjectTracker.Web.Services;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>The password e-mailed by "forgot password" must pass the same rule as a password typed by the user.</summary>
public class PasswordResetTests
{
    [Fact]
    public void Generated_password_passes_the_password_rule_and_mixes_every_character_class()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var password = PasswordResetService.Generate();
            Assert.Null(ProjectRules.PasswordProblem(password));
            Assert.Equal(12, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => "@#$%&*!?".Contains(c));
            Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
            seen.Add(password);
        }
        Assert.Equal(200, seen.Count);
    }
}
