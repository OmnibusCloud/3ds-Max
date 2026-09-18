using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxServerJobScopeResolverTests
{
    #region Resolve Tests

    [Test]
    public void TheWholeNetworkRightSubmitsUnscopedTest()
    {
        // The historic behaviour for accounts with the global grant, kept as is.
        var scope = MaxServerJobScopeResolver.Resolve(CreateScope(canRunOnAllClients: true, groups: ["Render Group"], projects: ["Bike Ride"]));

        Assert.Multiple(() =>
        {
            Assert.That(scope.IsResolved, Is.True);
            Assert.That(scope.UseAllClients, Is.True);
            Assert.That(scope.GroupName, Is.Empty);
            Assert.That(scope.ProjectName, Is.Empty);
        });
    }

    [Test]
    public void AGroupIsPreferredOverAProjectTest()
    {
        // The dialog listed projects FIRST and defaulted to the first entry, so a plain .blend export
        // was submitted into a campaign. The build never touches a node; the scope is permission only,
        // and a group grants it without dragging the export into a project.
        var scope = MaxServerJobScopeResolver.Resolve(CreateScope(groups: ["Render Group"], projects: ["Bike Ride"]));

        Assert.Multiple(() =>
        {
            Assert.That(scope.IsResolved, Is.True);
            Assert.That(scope.UseAllClients, Is.False);
            Assert.That(scope.GroupName, Is.EqualTo("Render Group"));
            Assert.That(scope.ProjectName, Is.Empty);
        });
    }

    [Test]
    public void AProjectIsTheLastResortTest()
    {
        var scope = MaxServerJobScopeResolver.Resolve(CreateScope(projects: ["Bike Ride"]));

        Assert.Multiple(() =>
        {
            Assert.That(scope.IsResolved, Is.True);
            Assert.That(scope.GroupName, Is.Empty);
            Assert.That(scope.ProjectName, Is.EqualTo("Bike Ride"));
        });
    }

    [Test]
    public void BlankNamesAreSkippedTest()
    {
        var scope = MaxServerJobScopeResolver.Resolve(CreateScope(groups: ["", "  ", "Artists"]));

        Assert.That(scope.GroupName, Is.EqualTo("Artists"));
    }

    [Test]
    public void AnAccountWithNoScopeCannotSubmitTest()
    {
        var scope = MaxServerJobScopeResolver.Resolve(CreateScope());

        Assert.That(scope.IsResolved, Is.False);
    }

    [Test]
    public void AFailedOrMissingScopeLoadCannotSubmitTest()
    {
        var failed = CreateScope(canRunOnAllClients: true, groups: ["Render Group"]);
        failed.IsSuccess = false;

        Assert.Multiple(() =>
        {
            Assert.That(MaxServerJobScopeResolver.Resolve(failed).IsResolved, Is.False);
            Assert.That(MaxServerJobScopeResolver.Resolve(null).IsResolved, Is.False);
        });
    }

    #endregion

    #region Tools

    private static MaxConnectedExecutionScopeResult CreateScope(
        bool canRunOnAllClients = false,
        string[]? groups = null,
        string[]? projects = null)
    {
        return new MaxConnectedExecutionScopeResult
        {
            IsSuccess = true,
            CanRunOnAllClients = canRunOnAllClients,
            Groups = (groups ?? []).Select(me => new MaxConnectedExecutionGroupOption { Name = me }).ToList(),
            Projects = (projects ?? []).Select(me => new MaxConnectedExecutionProjectOption { Name = me }).ToList()
        };
    }

    #endregion
}
