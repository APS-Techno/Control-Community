using APS.SystemControl.Hosting;

namespace CoreLogicTests;

public class FrameworkBoundaryTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Pomelo.EntityFrameworkCore.MySql")]
    public void FrameworkDoesNotReferenceClosedAssembly(string name)
        => Assert.DoesNotContain(typeof(ControlEdition).Assembly.GetReferencedAssemblies(), a => a.Name == name);
}
